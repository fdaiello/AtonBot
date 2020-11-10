// Notify
// Envia notificações para o usuario do Bot
//
using Microsoft.AspNetCore.Mvc;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Configuration;
using ContactCenter.Data;
using ContactCenter.Core.Models;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace MrBot.Controllers
{
	[Route("api/notify")]
	[ApiController]
	public class NotifyController : ControllerBase
	{
		private readonly IBotFrameworkHttpAdapter _adapter;
		private readonly string _appId;
		private readonly ApplicationDbContext _context;
		private string proactivetext = "";

		// Dependency injected dictionary for storing ConversationReference objects used in NotifyController to proactively message users
		private readonly ConcurrentDictionary<string, ConversationReference> _conversationReferences;

		public NotifyController(IBotFrameworkHttpAdapter adapter, IConfiguration configuration, ApplicationDbContext context, ConcurrentDictionary<string, ConversationReference> conversationReferences)
		{
			_adapter = adapter;
			_appId = configuration["MicrosoftAppId"];
			// Database Context
			_context = context;
			// Hash onde estão salvos pelo Bot as Conversations References
			_conversationReferences = conversationReferences;
		}

		public async Task<IActionResult> Get(string key, string id, string message)
		{

			if (string.IsNullOrEmpty(key) || key != "Micky-2020*")
				return new ContentResult()
				{
					Content = "<html><body>Unauthorized.</body></html>",
					ContentType = "text/html",
					StatusCode = (int)HttpStatusCode.Unauthorized,
				};
			else if (string.IsNullOrEmpty(id))
				return new ContentResult()
				{
					Content = "<html><body>ID not found.</body></html>",
					ContentType = "text/html",
					StatusCode = (int)HttpStatusCode.OK,
				};
			else if (string.IsNullOrEmpty(message))
				return new ContentResult()
				{
					Content = "<html><body>Message not found.</body></html>",
					ContentType = "text/html",
					StatusCode = (int)HttpStatusCode.OK,
				};
			else
			{
				Contact customer = _context.Contacts.Where(s => s.Id == id).FirstOrDefault();

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
							Content = "<html><body>Conversation reference not found for customer: " + id.ToString(CultureInfo.InvariantCulture) + ".</body></html>",
							ContentType = "text/html",
							StatusCode = (int)HttpStatusCode.OK,
						};

				}
				else
					// Let the caller know proactive messages have been sent
					return new ContentResult()
					{
						Content = "<html><body>Customer not found: " + id.ToString(CultureInfo.InvariantCulture) + ".</body></html>",
						ContentType = "text/html",
						StatusCode = (int)HttpStatusCode.OK,
					};
			}
		}

		private async Task BotCallback(ITurnContext turnContext, CancellationToken cancellationToken)
		{
			// If you encounter permission-related errors when sending this message, see
			// https://aka.ms/BotTrustServiceUrl
			await turnContext.SendActivityAsync(proactivetext).ConfigureAwait(false);
		}


	}
}
