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
using System.Threading.Tasks;
using System.Text;
using System.IO;
using PloomesApi;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Http;
using NETCore.MailKit.Core;
using Microsoft.VisualBasic;
using GsWhatsApp;
using Microsoft.Extensions.Logging;

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
		private readonly ILogger _logger;

		// Pra enviar emails
		private readonly IEmailService _EmailService;

		// Dependency injected dictionary for storing ConversationReference objects used in NotifyController to proactively message users
		private readonly ConcurrentDictionary<string, ConversationReference> _conversationReferences;

		public WebHookController(IBotFrameworkHttpAdapter adapter, IConfiguration configuration, BotDbContext botDbContext, ConcurrentDictionary<string, ConversationReference> conversationReferences, IEmailService emailService, GsWhatsAppClient gsWhatsAppClient, ILogger<WebHookController> logger)
		{
			_adapter = adapter;
			_botDbContext = botDbContext;
			_conversationReferences = conversationReferences;
			_EmailService = emailService;
			_gsWhatsAppClient = gsWhatsAppClient;
			_logger = logger;
			userid = configuration["MisterPostmanApi:userId"];
			token = configuration["MisterPostmanApi:token"];
		}

		public async Task<IActionResult> Post(string key)
		{
			string activityId;

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

						// Se informou que todos os dados dos tecnicos foram adicionados
						if ( apiDealWebhook.OldDeal.OtherProperties.DadosTecnicosVisitaAdicionados != AtonTecnicosVisitaInformados.Sim & apiDealWebhook.NewDeal.OtherProperties.DadosTecnicosVisitaAdicionados == AtonTecnicosVisitaInformados.Sim)
							// Monta a mensagem
							message = $"Oi {customer.Name}! Já temos o nome dos técnicos que farão a sua visita. Quando puder, entre em contato que lhe informo.";

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

						// Apos os dados dos técnicos que vão fazer a Instalacao terem sido informados
						else if (apiDealWebhook.OldDeal.OtherProperties.DadosTecnicosInstalacaoAdicionados != AtonTecnicosInstalacaoInformados.Sim  & apiDealWebhook.NewDeal.OtherProperties.DadosTecnicosInstalacaoAdicionados == AtonTecnicosInstalacaoInformados.Sim)
							// Monta a mensagem
							message = $"Oi {customer.Name}! Já temos o nome dos técnicos que farão a sua instalação. Quando puder, entre em contato que lhe informo.";

						// Após o boleto ter sido anexado
						else if (apiDealWebhook.OldDeal.OtherProperties.BoletoAttachmentId == 0 & apiDealWebhook.NewDeal.OtherProperties.BoletoAttachmentId != 0)
							// Monta a mensagem
							message = $"Oi {customer.Name}! O seu boleto de pagamento da segunda parcela já está disponível. Entre em contato para que eu possa lhe enviar.";

						// Se montou alguma mensagem
						if (!string.IsNullOrEmpty(message))
                        {
							// Se te os dados para envio pelo Whats
							if (!string.IsNullOrEmpty(whatsAppNumber) & !string.IsNullOrEmpty(customerWaNumber))
                            {
								// Se tem atividade em menos de 24 horas
								if (Utility.HoraLocal().Subtract(customer.LastActivity).TotalHours < 24)
									// Envia mensagem normal pelo WhatsApp
									activityId = await _gsWhatsAppClient.SendText(whatsAppNumber, customerWaNumber, message).ConfigureAwait(false);
								else
									// Envia HSM pelo WhatsApp
									activityId = await _gsWhatsAppClient.SendHsmText(whatsAppNumber, customerWaNumber, message).ConfigureAwait(false);

								// Se conseguiu enviar, e gerou messageid
								if (!string.IsNullOrEmpty(activityId))
                                {
									// Cria registro em ChattingLog
									ChattingLog chattingLog = new ChattingLog { Time = Utility.HoraLocal(), Source = MessageSource.Bot, Type = ChatMsgType.Text, CustomerId = customer.Id, ActivityId = activityId, GroupId = customer.GroupId };
									// Se passou de 24 horas, marca que é HSM
									if (Utility.HoraLocal().Subtract(customer.LastActivity).TotalHours >= 24)
										chattingLog.IsHsm = true;
									// e salva no banco
									_botDbContext.ChattingLogs.Add(chattingLog);
									await _botDbContext.SaveChangesAsync().ConfigureAwait(false);
								}
							}

                            // Envia por email
                            //try
                            //{
                            //    await _EmailService.SendAsync(customer.Email, "Aton Bot: notificação", message + "\n\nAton Bot\nwww.mpweb.me/-W123").ConfigureAwait(false);
                            //}
                            //catch (Exception ex)
                            //{
                            //    _logger.LogError(ex.Message);
                            //}

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
