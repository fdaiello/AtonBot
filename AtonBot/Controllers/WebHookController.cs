// WebHook
// Recebe eventos do Ploomes
//
using Microsoft.AspNetCore.Mvc;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Configuration;
using MrBot.Data;
using MrBot.Models;
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.IO;
using PloomesApi;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Http;
using NETCore.MailKit.Core;
using Microsoft.VisualBasic;
using GsWhatsApp;

namespace MrBot.Controllers
{
	[Route("api/webhook")]
	[ApiController]
	public class WebHookController : ControllerBase
	{
		private readonly IBotFrameworkHttpAdapter _adapter;
		private readonly BotDbContext _botDbContext;
		private readonly string userid;
		private readonly string token;
		private readonly GsWhatsAppClient _gsWhatsAppClient;

		// Pra enviar emails
		private readonly IEmailService _EmailService;

		// Dependency injected dictionary for storing ConversationReference objects used in NotifyController to proactively message users
		private readonly ConcurrentDictionary<string, ConversationReference> _conversationReferences;

		public WebHookController(IBotFrameworkHttpAdapter adapter, IConfiguration configuration, BotDbContext botDbContext, ConcurrentDictionary<string, ConversationReference> conversationReferences, IEmailService emailService, GsWhatsAppClient gsWhatsAppClient)
		{
			_adapter = adapter;
			_botDbContext = botDbContext;
			_conversationReferences = conversationReferences;
			_EmailService = emailService;
			_gsWhatsAppClient = gsWhatsAppClient;
			userid = configuration["MisterPostmanApi:userId"];
			token = configuration["MisterPostmanApi:token"];
		}

		public async Task<IActionResult> Post(string key)
		{

			if (string.IsNullOrEmpty(key) || key != "Mp-2020*yui")
				return new ContentResult()
				{
					Content = "<html><body>Unauthorized.</body></html>",
					ContentType = "text/html",
					StatusCode = (int)HttpStatusCode.Unauthorized,
				};
			else
			{

				if (Request.ContentType != null && Request.ContentType.StartsWith("application/json"))
                {

					// Desserializa o conteudo do POST.
					ApiDealWebhook apiDealWebhook = JsonConvert.DeserializeObject<ApiDealWebhook>(await GetRawBodyStringAsync(Request).ConfigureAwait(false));

					// Busca o ContactId
					
					int contactId = apiDealWebhook.NewDeal.ContactId;

					// Procura na base
					Customer customer = _botDbContext.Customers.Where(s => s.Tag1 == contactId.ToString()).FirstOrDefault();

					// Se achou o cliente na nossa base
					if (customer != null)
					{
						// Busca o whatsAppNumber do grupo do qual este customerid participa
						string whatsAppNumber = string.Empty ;
						Group uGroup = _botDbContext.Groups.Where(p => p.Id == customer.GroupId).FirstOrDefault();
						// Se nao achou
						if (uGroup != null)
							whatsAppNumber = uGroup.WhatsAppNumber;

						// Busca o telefone do WhatsApp do cliente - segunda parte do Id, apos o traco
						string customerWaNumber = string.Empty;
						if ( customer.Id.Split("-").Length==2)
							customerWaNumber = customer.Id.Split("-")[1];

						// Mensagem que vamos enviar
						string message = string.Empty;

						// Se preencheu nome do tecnico e documento do tecnico, para  visita
						if ((string.IsNullOrEmpty(apiDealWebhook.OldDeal.OtherProperties.TecnicoResposavel) | string.IsNullOrEmpty(apiDealWebhook.OldDeal.OtherProperties.DocumentoDoTecnico)) & !string.IsNullOrEmpty(apiDealWebhook.NewDeal.OtherProperties.TecnicoResposavel) & !string.IsNullOrEmpty(apiDealWebhook.NewDeal.OtherProperties.DocumentoDoTecnico))
							// Monta a mensagem
							message = $"Oi {customer.Name}! O técnico que fará a sua visita é {apiDealWebhook.NewDeal.OtherProperties.TecnicoResposavel}, documento: {apiDealWebhook.NewDeal.OtherProperties.DocumentoDoTecnico}";

						// Se foi validada a proposta
						else if (apiDealWebhook.OldDeal.OtherProperties.ResultadoValidacao != AtonResultadoValicacao.Validada & apiDealWebhook.NewDeal.OtherProperties.ResultadoValidacao == AtonResultadoValicacao.Validada)
						{
							// Monta a mensagem
							message = $"Oi {customer.Name}! Sua proposta está pronta. Me chame quando eu puder lhe enviar.";
							// Marca em Customer a data/hora que envioiu essa notificação - para enviar novamente 24 horas depois
							customer.Tag3 = DateAndTime.Now.ToString(new CultureInfo("en-US"));
							// Salva o cliente no banco
							_botDbContext.Customers.Update(customer);
							await _botDbContext.SaveChangesAsync().ConfigureAwait(false);
						}

						// 24 Horas depois que enviou a notificação da proposta, se ainda está no mesmo estágio, notifica novamente
						else if (apiDealWebhook.NewDeal.OtherProperties.ResultadoValidacao != AtonResultadoValicacao.Validada && apiDealWebhook.NewDeal.StageId == AtonStageId.ValidacaoDaVisitaeProposta && DateTime.TryParse(customer.Tag3, out DateTime dtNotificacaoProposta) && dtNotificacaoProposta.Subtract(DateTime.Now).TotalHours > 24)
						{
							// Monta a mensagem
							message = $"Oi {customer.Name}! Sua proposta está pronta. Me chame quando eu puder lhe enviar.";
							// Limpa tag 3 para não enviar novamente
							customer.Tag3 = string.Empty;
							// Salva o cliente no banco
							_botDbContext.Customers.Update(customer);
							await _botDbContext.SaveChangesAsync().ConfigureAwait(false);
						}

						// Quando for anexado o comprovante do primeiro pagamento
						else if (!apiDealWebhook.OldDeal.OtherProperties.Comprovante1aParcelaIdentificado & apiDealWebhook.NewDeal.OtherProperties.Comprovante1aParcelaIdentificado)
							// Monta a mensagem
							message = $"Oi {customer.Name}! Recebemos o seu comprovante de pagamento. Por favor, entre em contato para agendarmos sua instalação.";

						// Apos os dados dos técnicos que vão fazer a visita terem sido informados
						else if ((string.IsNullOrEmpty(apiDealWebhook.OldDeal.OtherProperties.NomeTecnico1) | string.IsNullOrEmpty(apiDealWebhook.OldDeal.OtherProperties.DocTecnico1) | string.IsNullOrEmpty(apiDealWebhook.OldDeal.OtherProperties.NomeTecnico2) | string.IsNullOrEmpty(apiDealWebhook.OldDeal.OtherProperties.DocTecnico2)) & !string.IsNullOrEmpty(apiDealWebhook.NewDeal.OtherProperties.NomeTecnico1) & !string.IsNullOrEmpty(apiDealWebhook.NewDeal.OtherProperties.DocTecnico1) & !string.IsNullOrEmpty(apiDealWebhook.NewDeal.OtherProperties.NomeTecnico2) & !string.IsNullOrEmpty(apiDealWebhook.NewDeal.OtherProperties.DocTecnico2))
							// Monta a mensagem
							message = $"Oi {customer.Name}! Já temos o nome dos técnicos que farão a sua instalação. Quando puder, entre em contato que lhe informo.";

						// Após o boleto ter sido anexado
						else if (apiDealWebhook.OldDeal.OtherProperties.BoletoAttachmentId == 0 & apiDealWebhook.NewDeal.OtherProperties.BoletoAttachmentId != 0)
							// Monta a mensagem
							message = $"Oi {customer.Name}! O seu boleto de pagamento da segunda parcela já está disponível. Entre em contato para que eu possa lhe enviar.";

						// Se montou alguma mensagem
						if (!string.IsNullOrEmpty(message))
                        {
							// Envia a mensagem direto via API do WhatsApp
							if ( !string.IsNullOrEmpty(whatsAppNumber) & !string.IsNullOrEmpty(customerWaNumber))
								await _gsWhatsAppClient.SendText(whatsAppNumber, customerWaNumber, message).ConfigureAwait(false);

							// Envia por email
							await _EmailService.SendAsync(customer.Email, "Aton Bot: notificação", message + "\n\nAton Bot\nwww.mpweb.me/-W123").ConfigureAwait(false);

							// Envia por SMS
							await Utility.SendSMS(userid, token, customer.MobilePhone, message + "\n\nAton Bot\nwww.mpweb.me/-W123").ConfigureAwait(false);

						}

						// Devolve resposta Http
						return new ContentResult()
						{
							Content = "<html><body>OK</body></html>",
							ContentType = "text/html",
							StatusCode = (int)HttpStatusCode.OK,
						};
					}
					else
                    {
						return new ContentResult()
						{
							Content = "<html><body>Customer not found at Bot Database.</body></html>",
							ContentType = "text/html",
							StatusCode = (int)HttpStatusCode.OK
						};
					}

				}
                else
                {
					return new ContentResult()
					{
						Content = "<html><body>ContentType is not Json</body></html>",
						ContentType = "text/html",
						StatusCode = (int)HttpStatusCode.OK,
					};

				}

			}
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
	}
}
