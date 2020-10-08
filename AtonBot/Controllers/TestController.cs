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
using System.Linq.Expressions;
using NETCore.MailKit.Infrastructure.Internal;

namespace MrBot.Controllers
{
	[Route("api/test")]
	[ApiController]
	public class TestController : ControllerBase
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

		public TestController(IBotFrameworkHttpAdapter adapter, IConfiguration configuration, BotDbContext botDbContext, ConcurrentDictionary<string, ConversationReference> conversationReferences, IEmailService emailService, GsWhatsAppClient gsWhatsAppClient)
		{
			_adapter = adapter;
			_botDbContext = botDbContext;
			_conversationReferences = conversationReferences;
			_EmailService = emailService;
			_gsWhatsAppClient = gsWhatsAppClient;
			userid = configuration["MisterPostmanApi:userId"];
			token = configuration["MisterPostmanApi:token"];
		}

		public async Task<IActionResult> Get()
		{

			//string whatsAppNumber = "551146733810";
			//string customerWaNumber = "555191096510";
			string message = "Oi Felipe! Sua proposta está pronta. Me chame quando eu puder lhe enviar.";
			string email = "felipedaiello@gmail.com";
			string result = "ok";

            //// Envia teste
            //string messageid = await _gsWhatsAppClient.SendHsmText(whatsAppNumber, customerWaNumber, message).ConfigureAwait(false);

            // Envia por email
            try
            {
				await _EmailService.SendAsync(email, "Aton Bot: notificação", message + "\n\nAton Bot\nwww.mpweb.me/-W123").ConfigureAwait(false);
			}
			catch (Exception ex)
            {
				result = ex.Message;
			}

			// Devolve resposta Http
			return new ContentResult()
			{
				Content = "<html><body>" + result + "</body></html>",
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
	}
}
