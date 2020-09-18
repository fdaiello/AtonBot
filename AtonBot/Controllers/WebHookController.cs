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
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using PloomesApi;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Http;
using System.Text;
using System.IO;
using System.ServiceModel.Channels;

namespace MrBot.Controllers
{
	[Route("api/webhook")]
	[ApiController]
	public class WebHookController : ControllerBase
	{
		private readonly IBotFrameworkHttpAdapter _adapter;
		private readonly string _appId;
		private readonly BotDbContext _context;
		private string proactivetext = "";

		// Dependency injected dictionary for storing ConversationReference objects used in NotifyController to proactively message users
		private readonly ConcurrentDictionary<string, ConversationReference> _conversationReferences;

		public WebHookController(IBotFrameworkHttpAdapter adapter, IConfiguration configuration, BotDbContext context, ConcurrentDictionary<string, ConversationReference> conversationReferences)
		{
			_adapter = adapter;
			_appId = configuration["MicrosoftAppId"];
			// Database Context
			_context = context;
			// Hash onde estão salvos pelo Bot as Conversations References
			_conversationReferences = conversationReferences;
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
					Customer customer = _context.Customers.Where(s => s.Tag1 == contactId.ToString()).FirstOrDefault();

					// Se achou o cliente na nossa base
					if (customer != null)
					{
						// Mensagem que vamos enviar
						string message = string.Empty;

						// Se preencheu nome do tecnico e documento do tecnico, para  visita
						if ((string.IsNullOrEmpty(apiDealWebhook.OldDeal.OtherProperties.TecnicoResposavel) | string.IsNullOrEmpty(apiDealWebhook.OldDeal.OtherProperties.DocumentoDoTecnico)) & !string.IsNullOrEmpty(apiDealWebhook.NewDeal.OtherProperties.TecnicoResposavel) & !string.IsNullOrEmpty(apiDealWebhook.NewDeal.OtherProperties.DocumentoDoTecnico))
							// Monta a mensagem
							message = $"Oi {customer.Name}! O técnico que fará a sua visita é {apiDealWebhook.NewDeal.OtherProperties.TecnicoResposavel}, documento: {apiDealWebhook.NewDeal.OtherProperties.DocumentoDoTecnico}";

						// Se foi validada a proposta
						else if (apiDealWebhook.OldDeal.OtherProperties.ResultadoValidacao != AtonResultadoValicacao.Validada & apiDealWebhook.NewDeal.OtherProperties.ResultadoValidacao == AtonResultadoValicacao.Validada)
							// Monta a mensagem
							message = $"Oi {customer.Name}! Sua proposta está pronta. Me chame quando eu puder lhe enviar.";

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
							/// Envia
							await SendProactiveMessage(customer.Id, message).ConfigureAwait(false);

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

		private async Task<IActionResult> SendProactiveMessage( string customerId, string message )
        {
			Customer customer = _context.Customers.Where(s => s.Id == customerId).FirstOrDefault();

			if (customer != null)
			{

				proactivetext = message;

				// Testa se a chave existe no dicionário
				if (_conversationReferences.ContainsKey(customer.Id))
				{
					// Busca o ConversationReference deste usuario
					ConversationReference conversationReference = _conversationReferences[customer.Id];

					// Envia uma notificação proativa
					await ((BotAdapter)_adapter).ContinueConversationAsync(_appId, conversationReference, BotCallback, default).ConfigureAwait(false);

					// Let the caller know proactive messages have been sent
					return new ContentResult()
					{
						Content = "<html><body>Proactive message have been sent.</body></html>",
						ContentType = "text/html",
						StatusCode = (int)HttpStatusCode.OK,
					};

				}
				else
					// Let the caller know proactive messages have been sent
					return new ContentResult()
					{
						Content = "<html><body>Conversation reference not found for customer: " + customerId.ToString(CultureInfo.InvariantCulture) + ".</body></html>",
						ContentType = "text/html",
						StatusCode = (int)HttpStatusCode.OK,
					};

			}
			else
				// Error - customer not found
				return new ContentResult()
				{
					Content = "<html><body>Customer not found: " + customerId.ToString(CultureInfo.InvariantCulture) + ".</body></html>",
					ContentType = "text/html",
					StatusCode = (int)HttpStatusCode.OK,
				};

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
		private async Task BotCallback(ITurnContext turnContext, CancellationToken cancellationToken)
		{
			// If you encounter permission-related errors when sending this message, see
			// https://aka.ms/BotTrustServiceUrl
			await turnContext.SendActivityAsync(proactivetext).ConfigureAwait(false);
		}

	}
}
