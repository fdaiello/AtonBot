// Helper para o WhatsApp Adapter
// Faz o meio de campo entre as requisições, a API do WhatsApp,
// e o serviços de reconhecimento e sintese de Voz
using Newtonsoft.Json;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Logging;
using GsWhatsApp;
using MrBot.Data;
using MrBot.Models;
using AdaptiveExpressions;

namespace GsWhatsAppAdapter
{
	/// <summary>
	/// A helper class to create Activities and WhatsApp messages.
	/// </summary>
	internal class WhatsAppHelper
	{
		// for testing purposes - querying via url query string
		internal bool querystringused;
		internal string textresponse;

		// Theese will be initialized when payload is receaved - Acording to GupShup App Name
		internal string whatsAppNumber;
		internal int groupID=1;

		internal static string heroCardButtonPin = "\U0001F4CD";

		private bool isspeechturn;

		private readonly Uri _botUri;
		private readonly GsWhatsAppClient _gsWhatsAppClient;
		private readonly SpeechClient _speechClient;
		private readonly CultureInfo culture = new CultureInfo("en-US");
		private readonly IConfiguration _configuration;

		internal WhatsAppHelper(GsWhatsAppClient gsWhatsAppClient, SpeechClient speechClient, Uri botUri, IConfiguration configuration)
		{
			isspeechturn = false;
			querystringused = false;
			textresponse = string.Empty;

			_speechClient = speechClient;
			_gsWhatsAppClient = gsWhatsAppClient;
			_botUri = botUri;
			_configuration = configuration;
		}
		/// <summary>
		/// Writes the HttpResponse.
		/// </summary>
		/// <param name="response">The httpResponse.</param>
		/// <param name="code">The status code to be written.</param>
		/// <param name="text">The text to be written.</param>
		/// <param name="encoding">The encoding for the text.</param>
		/// <param name="cancellationToken">A cancellation token for the task.</param>
		/// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
		internal async Task WriteAsync(HttpResponse response, int code, string text, Encoding encoding, CancellationToken cancellationToken)
		{
			if (response == null)
				throw new ArgumentNullException(nameof(response));

			if (text == null)
				throw new ArgumentNullException(nameof(text));

			if (encoding == null)
				throw new ArgumentNullException(nameof(encoding));

			response.ContentType = $"text/plain; charset={encoding.WebName}";
			response.StatusCode = code;

			text += textresponse;

			var data = encoding.GetBytes(text);

			await response.Body.WriteAsync(data, 0, data.Length, cancellationToken).ConfigureAwait(false);
		}
		/// <summary>
		/// Writes a black 200 OK
		/// </summary>
		/// <param name="response">The httpResponse.</param>
		/// <param name="cancellationToken">A cancellation token for the task.</param>
		/// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
		internal async Task WriteOkAsync(HttpResponse response, CancellationToken cancellationToken)
		{
			int statusCode = 200;
			string text = string.Empty;
			querystringused = false;
			textresponse = string.Empty;
			await WriteAsync(response, statusCode, text, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// Creates a Bot Framework <see cref="Activity"/> from an HTTP request that contains a GsWhatsApp message.
		/// </summary>
		/// <param name="payload">The HTTP request.</param>
		/// <returns>The activity object.</returns>
		internal async Task<Activity> PayloadToActivity(HttpRequest httpRequest, ILogger logger)
		{
			// flag que indica se usou query string - usado para testes
			querystringused = false;
			// buffer de resposta no Http - usado somente para testes
			textresponse = string.Empty;

			if (httpRequest == null)
				throw new ArgumentNullException(nameof(httpRequest));

			// Bot Activity que será construido com base na requisição http
			Activity activity;

			// Se veio payload 100% Json - GupShup Api V2
			if (httpRequest.ContentType != null && httpRequest.ContentType.StartsWith( "application/json"))
				activity = await JsonPayloadToActivity(httpRequest, logger).ConfigureAwait(false);

			// Confere se vieram parametros corretos por querystring - Testes
			else if (!string.IsNullOrEmpty(httpRequest.Query["text"]) && !string.IsNullOrEmpty(httpRequest.Query["from"]) && !string.IsNullOrEmpty(httpRequest.Query["appName"]) )
			{
				// marca em flag pra saber lidar com o retorno
				querystringused = true;
				isspeechturn = false;

				// Inicializa o GroupID e WhatsAppNumber
				await GetWhatsAppNumberNGroupIdFromAppName(httpRequest.Query["appName"]).ConfigureAwait(false);

				// Gera um ActivityID
				string activityId = "Qs" + DateTime.Now.ToString(CultureInfo.InvariantCulture);

				// monta atividade com base nos parametros query string
				activity = MessageActivityBuilder(activityId, httpRequest.Query["from"], httpRequest.Query["name"], "text", httpRequest.Query["text"], string.Empty, string.Empty);
			}

			// Erro
			else
			{
				// Grava em um arquivo de Log com a mensagem respondida
				logger.LogInformation("GsWhatsAppAdapter-" + DateTime.Today.ToString("G", new CultureInfo("pt-BR")), "Callback deve ser chamado via POST");
				throw new ArgumentException("Callback deve ser chamado via POST");
			}

			// Retorna a activity
			return activity;

		}

		/*
		 * Searchs Bot Database, table UGroup
		 * Finds BotName that matches appName passed as parameter
		 * Initializes internal varibles:
		 *    - WhatsAppNumber ( source number ) associated with that Bot
		 *    - GroupId
		 */
		internal async Task GetWhatsAppNumberNGroupIdFromAppName(string appName)
		{
			// Configurações para o banco de dados
			var optionsBuilder = new DbContextOptionsBuilder<BotDbContext>();
			optionsBuilder.UseSqlServer(_configuration.GetConnectionString("BotContext"),
					opts => opts.CommandTimeout((int)TimeSpan.FromSeconds(10).TotalSeconds));

			// Cria a referencia para o banco de dados
			BotDbContext botDbContext = new BotDbContext(optionsBuilder.Options);
			Group group = await botDbContext.Groups.Where(p => p.BotName == appName).FirstOrDefaultAsync().ConfigureAwait(false);
			botDbContext.Dispose();

			if (group != null)
            {
				whatsAppNumber = group.WhatsAppNumber;
				groupID = group.Id;
            }

			return;
		}
		/* 
		 * Check Paylod receaved in HttpRequest - according to GupShup V2 Api - https://www.gupshup.io/developer/docs/bot-platform/guide/whatsapp-api-documentation
		 * Create new Bot Activity with message or event acording to receaved information
		 */
		internal async Task<Activity> JsonPayloadToActivity(HttpRequest httpRequest, ILogger logger)
		{

			// Resseta flag que indica se tem que mandar audio de volta
			isspeechturn = false;

			// Bot Activity que será construido com base na requisição http
			Activity activity;

			// Mensagem Json
			string jsondata = await GetRawBodyStringAsync(httpRequest).ConfigureAwait(false);

			// Registra em Log os dados recebidos
			logger.LogInformation("GsWhatsAppAdapter-" + DateTime.Today.ToString("G", culture), Environment.NewLine + "in: " + jsondata);

			// Desserializa os dados recebidos como Json
			GsCallBack gsCallBack = JsonConvert.DeserializeObject<GsCallBack>(jsondata);

			// Procura o nome da aplicaçao que gerou o PayLoad
			if (!string.IsNullOrEmpty(gsCallBack.App))
				// Inicializa o GroupID e WhatsAppNumber
				await GetWhatsAppNumberNGroupIdFromAppName(gsCallBack.App).ConfigureAwait(false);

			else
				// Grava em um arquivo de Log indicando que não recebeu PayLoad com App name
				logger.LogError("GsWhatsAppAdapter-" + DateTime.Today.ToString("G", culture), $"App name not identified: {gsCallBack.Type}");

			// Se o tipo de mensagem é Evento
			if (gsCallBack.Type == "message-event")
				// constroi a Event-Activity com base no que veio na requisição
				activity = EventActivityBuilder(gsCallBack.Payload.GsId ?? gsCallBack.Payload.Id, gsCallBack.Payload.Type, gsCallBack.Payload.Destination, gsCallBack.App);

			// Confere se o tipo da mensagem é texto
			else if ( gsCallBack.Type=="message" && (gsCallBack.Payload.Type == "text" || gsCallBack.Payload.Type == "image" || gsCallBack.Payload.Type == "file"))
				// constroi a Activity com base no que veio na requisição
				activity = MessageActivityBuilder(gsCallBack.Payload.Id, gsCallBack.Payload.Sender.Phone, gsCallBack.Payload.Sender.Name, gsCallBack.Payload.Type, gsCallBack.Payload.Payload2.Text, gsCallBack.App, gsCallBack.Payload.Payload2.Url, gsCallBack.Payload.Payload2.Caption, gsCallBack.Payload.Context==null ? null : gsCallBack.Payload.Context.GsId);

			else if (gsCallBack.Type == "message" && gsCallBack.Payload.Type == "audio")
			{

				// Busca a stream com o audio
				Stream stream = await _gsWhatsAppClient.GetAudio(new Uri(gsCallBack.Payload.Payload2.Url)).ConfigureAwait(false);

				// Tenta reconhecer o Audio
				string speechtotext = await _speechClient.RecognizeOggStream(stream, gsCallBack.Payload.Id).ConfigureAwait(false);

				// Se Reconheceu com sucesso
				if (!string.IsNullOrEmpty(speechtotext) && speechtotext != "NOMATCH" && !speechtotext.StartsWith("CANCELED", StringComparison.OrdinalIgnoreCase))
					// Salva em variavel da classe que o turno tem conversa
					isspeechturn = true;
				else
					// Se não reconheceu, deixa o texto em branco
					speechtotext = string.Empty;

				// constroi a Activity com o audio e o texto
				activity = MessageActivityBuilder(gsCallBack.Payload.Id, gsCallBack.Payload.Sender.Phone, gsCallBack.Payload.Sender.Name, gsCallBack.Payload.Type, speechtotext, gsCallBack.App, gsCallBack.Payload.Payload2.Url, gsCallBack.Payload.Payload2.Caption);

			}
			else if (gsCallBack.Type == "user-event")
				// TODO: Manage USER Events
				// Opted In
				// Opted Out
				activity = null;

			else
			{
				// retorna Null
				activity = null;

				// Grava em um arquivo de Log indicando que veio um tipo não esperado
				logger.LogInformation("GsWhatsAppAdapter-" + DateTime.Today.ToString("G", culture), $"Tipo de mensagem não esperado: {gsCallBack.Type}");
			}

			return activity;
		}
		/// <summary>
		/// Retrieve the raw body as a string from the Request.Body stream
		/// </summary>
		/// <param name="request">Request instance to apply to</param>
		/// <param name="encoding">Optional - Encoding, defaults to UTF8</param>
		/// <returns></returns>
		public static async Task<string> GetRawBodyStringAsync(HttpRequest request, Encoding encoding = null)
		{
			if (encoding == null)
				encoding = Encoding.UTF8;

			using (StreamReader reader = new StreamReader(request.Body, encoding))
				return await reader.ReadToEndAsync().ConfigureAwait(false);
		}

		// Generates a Bot Activity with message to be passed to the Bot
		private Activity MessageActivityBuilder(string messageId, string from, string name, string type, string text, string botname, [Optional] string url, [Optional] string attachmentName, [Optional] string ContextId)
		{
			// Generates a customerID - based on GroupID + WhatsApp From number
			string customerId = groupID + "-" + from;

			// Instancia uma nova Activity
			Activity activity = new Activity
			{
				Id = messageId,
				Timestamp = DateTime.UtcNow,
				ChannelId = "whatsapp",
				Conversation = new ConversationAccount()
				{
					Id = customerId,
				},
				From = new ChannelAccount()
				{
					Id = customerId,
					Name = name
				},
				Recipient = new ChannelAccount()
				{
					Id = botname,
				},
				Type = ActivityTypes.Message,
			};

			activity.Text = text;

			// Se tem Id de "quotted" message
			if (ContextId != null)
				activity.ChannelData = ContextId;

			// Verifica o tipo da mensagem e os atributos que devem ser atribuidos
			if (type == "image")
			{
				activity.Attachments = new Attachment[1];
				activity.Attachments[0] = CreateAttachment(url, "image/png", attachmentName);
			}
			else if (type == "audio" | type == "voice" )
			{
				activity.Attachments = new Attachment[1];
				activity.Attachments[0] = CreateAttachment(url, "audio/ogg", attachmentName);
			}
			else if (type == "file")
			{
				activity.Attachments = new Attachment[1];
				activity.Attachments[0] = CreateAttachment(url, "application/pdf", attachmentName);
			}
			else if (type == "event")
			{
				activity.Type = ActivityTypes.Event;
				activity.Attachments = null;
			}
			return (activity);

		}
		// Envia um evento para o Bot
		private static Activity EventActivityBuilder(string messageId, string value, string from, string botname)
		{
			// Instancia uma nova Activity
			Activity activity = new Activity
			{
				Id = messageId,
				Timestamp = DateTime.UtcNow,
				ChannelId = "whatsapp",
				Type = ActivityTypes.Event,
				Value = value,
				Conversation = new ConversationAccount()
				{
					Id = from,
				},
				From = new ChannelAccount()
				{
					Id = from,
				},
				Recipient = new ChannelAccount()
				{
					Id = botname,
				},
			};

			return (activity);

		}
		// Envia uma atividade para o WhatsApp
		public async Task<string> SendActivityToWhatsApp(Activity activity)
		{
			// Busca o número de destino
			string recipient = activity.Recipient.Id;
			// Se tiver identificaçao do grupo na frente, busca o numero que vem depois do simbolo -
			if (recipient.Contains("-"))
				recipient = recipient.Split("-")[1];

			// Se for uma mensagem de texto
			if (activity.Text != null)
			{
				// Recupera a mensagem
				string reply = activity.Text;

				// Se está conversando por Audio
				if (isspeechturn)
					// envia o texto como Audio
					activity.Id = await SendVoice(activity.Text, activity.Id, recipient).ConfigureAwait(false);

				// Confere se tem Suggested Actions ( ações sugeridas )
				if (activity.SuggestedActions != null && activity.SuggestedActions.Actions.Any())
				{
					// Adiciona a resposta as ações sugeridas
					foreach (CardAction sugestedaction in activity.SuggestedActions.Actions)
					{
						reply += "\n     ```" + sugestedaction.Title + "```";
					}
				}

				// Envia a mensagem recebida do Bot de volta para o usuario
				if (querystringused)
				{
					// Se veio requisição por Query String ( geralmente teste ), devolve na requisição http
					if (!string.IsNullOrEmpty(textresponse)) textresponse += "\n";
					textresponse += reply;
				}
				else
					// Se não é teste, envia via API ( se enviar na mesma requisição, fica grudado na mesma mensagem )
					activity.Id = await _gsWhatsAppClient.SendText(whatsAppNumber, recipient, reply).ConfigureAwait(false);

			}

			// Are there any attachments?
			if (activity.Attachments != null)
			{
				// Extract each attachment from the activity.
				foreach (Attachment attachment in activity.Attachments)
				{
					// Processa anexos recebidos do BOT e que devem ser enviados para o cliente
					switch (attachment.ContentType)
					{
						case "image/png":
						case "image/jpg":
						case "image/jpeg":
							// Chama o método para enviar a imagem para o cliente via Gushup API
							activity.Id = await _gsWhatsAppClient.SendMedia(whatsAppNumber, recipient, GsWhatsAppClient.Mediatype.image, attachment.Name, new Uri(attachment.ContentUrl), attachment.ThumbnailUrl == null ? null : new Uri(attachment.ThumbnailUrl)).ConfigureAwait(false);
							break;

						case "application/pdf":
							// Chama o método para enviar o video para o cliente via Gushup API
							activity.Id = await _gsWhatsAppClient.SendMedia(whatsAppNumber, recipient, GsWhatsAppClient.Mediatype.file, attachment.Name, new Uri(attachment.ContentUrl)).ConfigureAwait(false);
							break;

						case "video/mpeg":
							// Chama o método para enviar o video para o cliente via Gushup API
							activity.Id = await _gsWhatsAppClient.SendMedia(whatsAppNumber, recipient, GsWhatsAppClient.Mediatype.video, attachment.Name, new Uri(attachment.ContentUrl)).ConfigureAwait(false);
							break;

						case "audio/ogg":
							// Chama o método para enviar o video para o cliente via Gushup API
							activity.Id = await _gsWhatsAppClient.SendMedia(whatsAppNumber, recipient, GsWhatsAppClient.Mediatype.audio, attachment.Name, new Uri(attachment.ContentUrl)).ConfigureAwait(false);
							break;

						case "audio/mp3":
							// Chama o método para enviar o video para o cliente via Gushup API
							activity.Id = await _gsWhatsAppClient.SendMedia(whatsAppNumber, recipient, GsWhatsAppClient.Mediatype.audio, attachment.Name, new Uri(attachment.ContentUrl)).ConfigureAwait(false);
							break;

						case "application/vnd.microsoft.card.hero":
							// Se é conversa por audio
							if (isspeechturn)
								// Envia o texto do HeroCard por audio
								activity.Id = await SendVoiceFromHeroText(attachment, activity.Id, recipient).ConfigureAwait(false);

							// Se é teste 
							if (querystringused)
							{
								// devolve via http
								if (!string.IsNullOrEmpty(textresponse)) textresponse += "\n";
								textresponse += ConvertHeroCardToWhatsApp(attachment);
							}
							else
								// envia para o cliente via API
								activity.Id = await _gsWhatsAppClient.SendText(whatsAppNumber, recipient, ConvertHeroCardToWhatsApp(attachment)).ConfigureAwait(false);

							break;

					}
				}
			}

			return activity.Id;
		}

		// Converte um HeroCard em texto puro
		private static string ConvertHeroCardToWhatsApp(Attachment attachment)
		{


			/// Hero Card está perdendo os Buttons na desserialização
			var heroCard = JsonConvert.DeserializeObject<HeroCard>(JsonConvert.SerializeObject(attachment.Content));

			string waoutput = "";

			if (heroCard != null)
			{
				if (!string.IsNullOrEmpty(heroCard.Title) && !heroCard.Title.StartsWith("*"))
					waoutput += "*" + heroCard.Title + "*\n";

				if (!string.IsNullOrEmpty(heroCard.Text))
					waoutput += heroCard.Text + "\n";

				if (!string.IsNullOrEmpty(waoutput))
					waoutput += "\n";

				if (heroCard.Buttons != null)
					foreach (CardAction button in heroCard.Buttons)
						waoutput += BoldFirstDigit(button.Title) + "\n";

				if (heroCard.Images != null)
					foreach (CardImage image in heroCard.Images)
						waoutput += image.Url + "\n";
			}
			return waoutput;
		}

		// Pega o título de um Hero Card e envia como Voz
		private async Task<string> SendVoiceFromHeroText(Attachment herocard, string textid, string usernumber)
		{

			var heroCard = JsonConvert.DeserializeObject<HeroCard>(JsonConvert.SerializeObject(herocard.Content));
			if (heroCard != null)
			{
				if (!string.IsNullOrEmpty(heroCard.Text))
					return await SendVoice(heroCard.Text, textid, usernumber).ConfigureAwait(false);
			}

			return string.Empty;
		}
		// Se a linha começa com um digito ( padrão de menu )...
		//    Adiciona um asterisco antes e outro depois do numero - negrito no whats app
		private static string BoldFirstDigit(string line)
		{
			if (line.Substring(1, 1).IsNumber())
				return line.Substring(0, 1).Replace("1", "*1*").Replace("2", "*2*").Replace("3", "*3*").Replace("4", "*4*").Replace("5", "*5*").Replace("6", "*6*").Replace("7", "*7*").Replace("8", "*8*").Replace("9", "*9*") + line.Substring(1);
			else
				return heroCardButtonPin + line;
		}

		// Creates an <see cref="Attachment"/> to be sent from the bot to the user from a HTTP URL.
		private static Attachment CreateAttachment(string url, string contenttype, [Optional] string caption)
		{

			// Busca o nome, da URL, ou do caption
			string name = url.Split('/').Last();
			if (name.Split('.').Length == 1)
				if (caption != null)
					name = caption;
				else
					if (contenttype == "image/png")
						name += ".png";

			// ContentUrl must be HTTPS.
			Attachment attachment =  new Attachment
			{
				Name = name,
				ContentType = contenttype,
				ContentUrl = url
			};

			return attachment;
		}

		private static byte[] ConverteStreamToByteArray(Stream stream)
		{
			using MemoryStream mStream = new MemoryStream();
			if ( stream != null && stream.Length >0 )
				stream.CopyTo(mStream);
			return mStream.ToArray();
		}
		// Converte um texto em Audio, converte para MP3, e envia
		private async Task<string> SendVoice(string text, string voiceid, string usernumber)
		{
			// Se não recebeu um textid ( necessario pra gerar o nome dos arquivos ) - gera um
			if (voiceid == null)
				voiceid = Guid.NewGuid().ToString("N", new CultureInfo("en-US"));

			// Retira caracteres Unicode ( pra não falar os Emojis )
			const int MaxAnsiCode = 255;
			if (text.Any(c => c > MaxAnsiCode))
			{
				string cleantext = string.Empty;
				for (int x = 0; x < text.Length; x++)
					if ((int)text[x] <= MaxAnsiCode)
						cleantext += text[x];
				text = cleantext;
			}

			// Retira negrito
			text = text.Replace("**", "");

			// Tenta converter o texto para audio
			if (await _speechClient.TextToSpeechAsync(text, voiceid).ConfigureAwait(false))
			{
				// Se conseguiu sintetizar o audio com base no texto, converte Wav pra Ogg
				string filenamewav = Path.Combine(Environment.CurrentDirectory, $@"wwwroot\media\Audio_{voiceid}.wav");
				Mp3Converter.WaveToMP3(filenamewav, filenamewav.Replace(".wav", ".mp3", StringComparison.OrdinalIgnoreCase));

				// Chama o método para enviar o video para o cliente via Gushup API
			    Uri mediaUri = new Uri (_botUri,  $@"/media/Audio_{voiceid}.mp3");
				return await _gsWhatsAppClient.SendMedia(whatsAppNumber, usernumber, GsWhatsAppClient.Mediatype.audio, filenamewav.Replace(".wav", "", StringComparison.OrdinalIgnoreCase), mediaUri).ConfigureAwait(false);
			}
			else
				return string.Empty;

		}
	}
	// Mensagem GupShup vinda no CallBAck da API - V2
	public class GsCallBack
	{
		[JsonProperty(PropertyName = "app")]
		public string App { get; set; }
		[JsonProperty(PropertyName = "timestamp")]
		public long Timestamp { get; set; }
		[JsonProperty(PropertyName = "version")]
		public int Version { get; set; }
		[JsonProperty(PropertyName = "type")]
		public string Type { get; set; }
		[JsonProperty(PropertyName = "payload")]
		public Payload Payload { get; set; }

	}
	public class Payload
	{
		[JsonProperty(PropertyName = "id")]
		public string Id { get; set; }
		[JsonProperty(PropertyName = "gsId")]
		public string GsId { get; set; }
		[JsonProperty(PropertyName = "source")]
		public string Source { get; set; }
		[JsonProperty(PropertyName = "type")]
		public string Type { get; set; }
		[JsonProperty(PropertyName = "payload")]
		public Payload2 Payload2 { get; set; }
		[JsonProperty(PropertyName = "sender")]
		public Sender Sender { get; set; }
		[JsonProperty(PropertyName = "context")]
		public Context Context { get; set; }
		[JsonProperty(PropertyName = "destination")]
		public string Destination { get; set; }

	}
	public class Sender
	{
		[JsonProperty(PropertyName = "phone")]
		public string Phone { get; set; }
		[JsonProperty(PropertyName = "name")]
		public string Name { get; set; }
		[JsonProperty(PropertyName = "country_code")]
		public string CountryCode { get; set; }
		[JsonProperty(PropertyName = "dial_code")]
		public string DialCode { get; set; }
	}
	public class Context
    {
		[JsonProperty(PropertyName = "id")]
		public string Id { get; set; }
		[JsonProperty(PropertyName = "gsId")]
		public string GsId { get; set; }

	}
	public class Payload2
	{
		[JsonProperty(PropertyName = "text")]
		public string Text { get; set; }
		[JsonProperty(PropertyName = "caption")]
		public string Caption { get; set; }
		[JsonProperty(PropertyName = "url")]
		public string Url { get; set; }
		[JsonProperty(PropertyName = "urlExpiry")]
		public long UrlExpiry { get; set; }

	}
	// Mensagem GupShup vinda no CallBAck da API - V1
	public class GsMessageObj
	{
		public string From { get; set; }
		[JsonProperty(PropertyName = "id")]
		public string Id { get; set; }
		[JsonProperty(PropertyName = "gsId")]
		public string GsId { get; set; }
		public string Text { get; set; }
		public string Timestamp { get; set; }
		public string Type { get; set; }
		public string Url { get; set; }
		public string Imgdata { get; set; }
		public GsVoice Voice { get; set; }
		[JsonProperty(PropertyName = "caption")]
		public string Caption { get; set; }
		[JsonProperty(PropertyName = "status")]
		public string Status { get; set; }
	}
	// Mensagem de Voz - padrão GupShup
	public class GsVoice
	{
		public string Id { get; set; }
		[JsonProperty(PropertyName = "Mime_type")]
		public string Mimetype { get; set; }
		public string Sha256 { get; set; }
	}
	public class GsEventData
	{
#pragma warning disable CA1707
		public string gs_id { get; set; }					// Only used to desserialize Json object receaved from gsAPI. We will kepp names as originals
		public string id { get; set; }
		public string recipient_id { get; set; }
#pragma warning restore CA1707
		public string senderName { get; set; }
		public string status { get; set; }
		public int timestamp { get; set; }

	}
	public class GsEvent
	{
		public GsEventData data { get; set; }
		public string text { get; set; }
		public string type { get; set; }

	}
}