// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
//
// Generated with Bot Builder V4 SDK Template for Visual Studio EchoBot v4.6.2

using MisterBot.Infrastructure.Adaters.GsWhatsApp;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using System.Threading.Tasks;

namespace MrBot.Controllers
{
	// This ASP Controller is created to handle a request. Dependency Injection will provide the Adapter and IBot
	// implementation at runtime. Multiple different IBot implementations running at different endpoints can be
	// achieved by specifying a more specific type for the bot constructor argument.
	[Route("api/whatsapp")]
	[ApiController]
	public class WhatsAppBotController : ControllerBase
	{
		private readonly IBotFrameworkHttpAdapter Adapter;
		private readonly IBot Bot;

		public WhatsAppBotController(GsWhatsAppAdapter adapter, IBot bot)
		{
			Adapter = adapter;
			Bot = bot;
		}

		[HttpPost, HttpGet]
		public async Task PostAsync()
		{
			// Delegate the processing of the HTTP POST to the adapter.
			// The adapter will invoke the bot.
			await Adapter.ProcessAsync(Request, Response, Bot).ConfigureAwait(false);
		}
	}
}
