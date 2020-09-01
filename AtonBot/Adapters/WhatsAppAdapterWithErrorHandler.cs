// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
//
// Generated with Bot Builder V4 SDK Template for Visual Studio CoreBot v4.6.2

using System;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.ApplicationInsights.Core;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Builder.TraceExtensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using MrBot.Data;
using MrBot.Models;
using GsWhatsAppAdapter;

namespace MrBot
{
	public class WhatsAppAdapterWithErrorHandler : WhatsAppAdapter
	{
		private readonly IConfiguration _configuration;
		public WhatsAppAdapterWithErrorHandler(
			TranscriptLoggerMiddleware transcriptMiddleware,
			IConfiguration configuration,
			ILogger<BotFrameworkHttpAdapter> logger,
			TelemetryInitializerMiddleware telemetryInitializerMiddleware,
			IBotTelemetryClient telemetryClient,
			ConversationState conversationState
			)
			: base(configuration,  logger)
		{

			_configuration = configuration;

			// Middleware for logging all messages to database
			Use(transcriptMiddleware);

			OnTurnError = async (turnContext, exception) =>
			{
				// Log any leaked exception from the application.
				logger.LogError(exception, $"[OnTurnError] unhandled error : {exception.Message}");

				// Telemetry error track
				telemetryClient.TrackException(exception);

				// Send a message to the user
				await turnContext.SendActivityAsync("Foi mal ... estou enfrentando um problema no meu servidor.").ConfigureAwait(false);
				await turnContext.SendActivityAsync("Vamos tentar recomeçar pra ver se da certo. Por favor, tecle: menu").ConfigureAwait(false);

				// Send a trace activity, which will be displayed in the Bot Framework Emulator
				await turnContext.TraceActivityAsync("OnTurnError Trace", exception.Message, "https://www.botframework.com/schemas/error", "TurnError").ConfigureAwait(false);

				// Registra no banco o erro
				await LogBotMessageToDatabase(turnContext.Activity.Text, turnContext.Activity.From.Id, MessageSource.Customer).ConfigureAwait(false);
				await LogBotMessageToDatabase("Foi mal ... estou enfrentando um problema no meu servidor.", turnContext.Activity.From.Id, MessageSource.Bot).ConfigureAwait(false);
				await LogBotMessageToDatabase("Vamos tentar recomeçar pra ver se da certo. Por favor, tecle: menu", turnContext.Activity.From.Id, MessageSource.Bot).ConfigureAwait(false);
				await LogBotMessageToDatabase(exception.Message, turnContext.Activity.From.Id, MessageSource.Bot).ConfigureAwait(false);

				// Se tem inner exception
				if ( exception.InnerException != null)
				{
					logger.LogError(exception, $"{exception.InnerException}");
					await LogBotMessageToDatabase(exception.InnerException.ToString(), turnContext.Activity.From.Id, MessageSource.Bot).ConfigureAwait(false);
				}

				if (conversationState != null)
				{
					try
					{
						// Delete the conversationState for the current conversation to prevent the
						// bot from getting stuck in a error-loop caused by being in a bad state.
						// ConversationState should be thought of as similar to "cookie-state" in a Web pages.
						await conversationState.DeleteAsync(turnContext).ConfigureAwait(false);
					}
					catch (Exception e)
					{
						logger.LogError(e, $"Exception caught on attempting to Delete ConversationState : {e.Message}");
					}
				}
			};

			// Telemetry
			Use(telemetryInitializerMiddleware);

		}
		private async Task LogBotMessageToDatabase( string textmessage, string customerid, MessageSource source)
		{
			// Configurações para o banco de dados
			var optionsBuilder = new DbContextOptionsBuilder<BotDbContext>();
			optionsBuilder.UseSqlServer(_configuration.GetConnectionString("BotContext"),
					opts => opts.CommandTimeout((int)TimeSpan.FromSeconds(10).TotalSeconds));

			// Cria a referencia para o banco de dados
			BotDbContext botDbContext = new BotDbContext(optionsBuilder.Options);

			// Insere a mensagem
			ChattingLog chattingLog = new ChattingLog { Text = textmessage, Time = Utility.HoraLocal(), Source = source, Type = ChatMsgType.Text, CustomerId = customerid };
			botDbContext.ChattingLogs.Add(chattingLog);
			await botDbContext.SaveChangesAsync().ConfigureAwait(false);

			botDbContext.Dispose();

			// returno
			return;
		}
	}

}
