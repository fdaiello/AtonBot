using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MrBot.Data;
using MrBot.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MrBot.Middleware
{
	public class BotDbContextTranscriptStore : ITranscriptLogger
	{

		// Logger
		private readonly ILogger _logger;

		private readonly DbContextOptionsBuilder<BotDbContext> _optionsBuilder;
		public BotDbContextTranscriptStore(DbContextOptionsBuilder<BotDbContext> options, ILogger logger)
		{
			_optionsBuilder = options;
			_logger = logger;

		}
		public async Task LogActivityAsync(IActivity activity)
		{
			// activity only contains Text if this is a message
			var isMessage = activity.AsMessageActivity() != null ? true : false;
			if (isMessage)
			{

				// Indica a origem da mensagem: Customer / Bot
				MessageSource source;

				// Salva o CustomerID
				string customerid;

				// Verifica se o destinatario da mensagem é o cliente
				if (activity.Recipient.Role == "user")
				{
					// Se o destino é o cliente, marca o Bot como sendo origem
					source = MessageSource.Bot;
					// E o customerid é o destinatário da mensagem
					customerid = activity.Recipient.Id;
				}
				else
				{
					// marca o Customer como sendo a origem
					source = MessageSource.Customer;
					// E o customerid é o remetente da mensagem
					customerid = activity.From.Id;
				}

				BotDbContext botDbContext = new BotDbContext(_optionsBuilder.Options);

				// Insere a as mensagens desta Activity no banco de dados
				try
				{
					// guarda a ultima mensagem enviada ou recebida pelo usuário
					string lasttext = "";

					// Atualiza last activity do cliente
					Customer customer = botDbContext.Customers.Where(s => s.Id == customerid).FirstOrDefault();
					if (customer != null)
					{
						customer.LastActivity = Utility.HoraLocal();

						// Se está recebendo mensagem do Agente
						if (customer.Status == CustomerStatus.TalkingToAgent & source != MessageSource.Customer)
							// Zera contador de mensagens
							customer.UnAnsweredCount = 0;

						// Se não é mensagem vindo do agente - o ChatBotApp salva no banco primeiro
						else
						{
							// Extrai todas as mensagens da Activity
							foreach (Message message in GetMessagesList(activity.AsMessageActivity()))
							{
								lasttext = message.Text;

								// Cria um objeto chattinglog com os dados recebidos
								ChattingLog chattingLog = new ChattingLog { Time = Utility.HoraLocal(), Source = source, Type = message.Type, CustomerId = customerid, ActivityId= activity.Id, GroupId = customer.GroupId };
								if (message.Type == ChatMsgType.Text)
									chattingLog.Text = message.Text;
								else if ( message.Type == ChatMsgType.Location)
								{
									chattingLog.Text = message.Text;
									lasttext = "location";
								}
								else if (message.Type == ChatMsgType.Contacts)
								{
									chattingLog.Text = message.Text;
									lasttext = "contact";
								}
								else
								{
									chattingLog.Filename = message.FileName;
									chattingLog.Text = string.Empty;
									if (message.Type ==ChatMsgType.Voice)
										lasttext = "audio";
									else
										lasttext = "file";
								}

								// Se tem contexto - "quoted" message
								if (activity.ChannelId == "whatsapp" && activity.ChannelData != null)
									chattingLog.QuotedActivityId = activity.ChannelData	;

								// Insere no banco
								if ( !string.IsNullOrEmpty(message.Text) | !string.IsNullOrEmpty(message.FileName))
									botDbContext.ChattingLogs.Add(chattingLog);
							}
							// Atualiza a ultima mensagem recebida direto no registro do cliente
							customer.LastText = lasttext;

						}

						// Se está enviando mensagem pro Agente
						if (customer.Status == CustomerStatus.TalkingToAgent & source == MessageSource.Customer)
							// Incrementa contador de mensagens não respondidas
							customer.UnAnsweredCount += 1;

						// Salva
						botDbContext.Customers.Update(customer);
						await botDbContext.SaveChangesAsync().ConfigureAwait(false);
					}

				}
				catch (Exception e)
				{
					// Log
					_logger.LogError(e.Message);

				}

				botDbContext.Dispose();

			}

			// Event Activity
			else if ( activity.Type=="event" & activity.ChannelId=="whatsapp")
			{
				string value = activity.AsEventActivity().Value.ToString();

				BotDbContext botDbContext = new BotDbContext(_optionsBuilder.Options);
				ChattingLog chattingLog = botDbContext.ChattingLogs.Where(p => p.ActivityId == activity.Id).FirstOrDefault();

				if ( chattingLog != null)
				{
					switch (value)
					{
						case "enqueued":
							chattingLog.Status = MsgStatus.Enqueued;
							break;
						case "sent":
							chattingLog.Status = MsgStatus.Sent;
							break;
						case "delivered":
							chattingLog.Status = MsgStatus.Delivered;
							break;
						case "read":
							chattingLog.Status = MsgStatus.Read;
							break;
						case "failed":
							chattingLog.Status = MsgStatus.Failed;
							break;
					}

					chattingLog.StatusTime = Utility.HoraLocal();

					botDbContext.ChattingLogs.Update(chattingLog);
					await botDbContext.SaveChangesAsync().ConfigureAwait(false);

				}
				botDbContext.Dispose();
			}

		}
		private static List<Message> GetMessagesList(IMessageActivity activity)
		{
			// Cria uma lista para guardar as mensagens extraidas da Activity
			List<Message> messages = new List<Message>();

			// Se for uma mensagem de texto
			if (activity.Text != null)
			{
				// Recupera a mensagem
				string text = activity.Text;

				// Confere se tem Suggested Actions ( ações sugeridas )
				if (activity.SuggestedActions != null && activity.SuggestedActions.Actions.Count > 0)
				{
					// Adiciona a resposta as ações sugeridas
					foreach (CardAction sugestedaction in activity.SuggestedActions.Actions)
					{
						text += "\n     ```" + sugestedaction.Title + "```";
					}
				}

				// Adiciona a lista
				messages.Add(new Message { Text = text, Type = ChatMsgType.Text });
			}

			// Are there any attachments?
			if (activity.Attachments != null)
			{
				// Nome do arquivo
				string filename;

				// Extract each attachment from the activity.
				foreach (Attachment attachment in activity.Attachments)
				{
					// Se é uma mensagem enviada para o usuário
					if (activity.Recipient.Role == "user")
						// Altera o nome para salvar a URL do arquivo
						filename = attachment.ContentUrl;

					// Se é eviada do usuário pro Bot
					else
						// Altera o nome do arquivo com o mesmo padrão usado para salvar no cloud storage
						filename = Utility.UniqueFileName(activity, attachment.Name);

					// Processa anexos recebidos do BOT e que devem ser enviados para o cliente
					switch (attachment.ContentType)
					{
						case "image/png":
						case "image/jpg":
						case "image/jpeg":
							// Adiciona a URL da media a lista de mensagens
							messages.Add(new Message { FileName = filename, Type = ChatMsgType.Image });
							break;

						case "application/pdf":
							// Adiciona a URL da media a lista de mensagens
							messages.Add(new Message { FileName = filename, Type = ChatMsgType.PDF });
							break;

						case "application/vnd.openxmlformats-officedocument.wordprocessingml.document":
							// Adiciona a URL da media a lista de mensagens
							messages.Add(new Message { FileName = filename, Type = ChatMsgType.Word });
							break;

						case "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet":
							// Adiciona a URL da media a lista de mensagens
							messages.Add(new Message { FileName = filename, Type = ChatMsgType.Excel });
							break;

						case "video/mpeg":
							// Adiciona a URL da media a lista de mensagens
							messages.Add(new Message { FileName = filename, Type = ChatMsgType.File });
							break;

						case "audio/ogg":
							// Adiciona a URL da media a lista de mensagens
							messages.Add(new Message { FileName = filename, Type = ChatMsgType.Voice });
							break;

						case "audio/mp3":
							// Adiciona a URL da media a lista de mensagens
							messages.Add(new Message { FileName = filename, Type = ChatMsgType.Voice });
							break;

						case "application/vnd.microsoft.card.hero":
							// Converte o anexo em texto, e adiciona a lista de mensagens
							messages.Add(new Message { Text = ConvertHeroCardToText(attachment), Type = ChatMsgType.Text });
							break;

						case "application/json":
							// Salva objeto json no campo texto
							if (attachment.Name == "location")
								messages.Add(new Message { Text = attachment.Content.ToString(), Type = ChatMsgType.Location, FileName= ChatMsgType.Location.ToString() });
							else if ( attachment.Name =="contact")
								messages.Add(new Message { Text = attachment.Content.ToString(), Type = ChatMsgType.Contacts, FileName = ChatMsgType.Contacts.ToString() });
							break;
					}
				}
			}

			return (messages);
		}
		private static string ConvertHeroCardToText(Attachment attachment)
		{

			var heroCard = JsonConvert.DeserializeObject<HeroCard>(attachment.Content.ToString());
			string waoutput = "";

			if (heroCard != null)
			{
				if (!string.IsNullOrEmpty(heroCard.Title))
					waoutput += heroCard.Title + "\n";

				if (!string.IsNullOrEmpty(heroCard.Text))
					waoutput += "*" + heroCard.Text + "*\n";

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
		private static string BoldFirstDigit(string line)
		{
			return line.Substring(0, 1).Replace("1", "*1*").Replace("2", "*2*").Replace("3", "*3*").Replace("4", "*4*").Replace("5", "*5*").Replace("6", "*6*").Replace("7", "*7*").Replace("8", "*8*").Replace("9", "*9*") + line.Substring(1);
		}

		class Message
		{
			public string Text { get; set; }
			public string FileName { get; set; }
			public ChatMsgType Type { get; set; }
		}

	}

}
