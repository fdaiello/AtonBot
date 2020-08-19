// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
//
// Generated with Bot Builder V4 SDK Template for Visual Studio CoreBot v4.6.2

using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.ApplicationInsights.Core;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Builder.TraceExtensions;
using Microsoft.Bot.Solutions.Feedback;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;

namespace MrBot
{
	public class AdapterWithTranscriptAndErrorHandler : BotFrameworkHttpAdapter
	{
		public AdapterWithTranscriptAndErrorHandler(
			TranscriptLoggerMiddleware transcriptMiddleware,
			IConfiguration configuration,
			ILogger<BotFrameworkHttpAdapter> logger,
			TelemetryInitializerMiddleware telemetryInitializerMiddleware,
			IBotTelemetryClient telemetryClient,
			ConversationState conversationState = null)
			: base(configuration, logger)
		{
			// Middleware for logging all messages to database
			Use(transcriptMiddleware);

			OnTurnError = async (turnContext, exception) =>
			{
				// Log any leaked exception from the application.
				logger.LogError(exception, $"[OnTurnError] unhandled error : {exception.Message}");

				// Telemetry error track
				telemetryClient.TrackException(exception);

				// Send a message to the user
				await turnContext.SendActivityAsync("Foi mal ... ocorreu um problema que não sei resolver. \n\n " + exception.Message).ConfigureAwait(false);
				await turnContext.SendActivityAsync("Vou ter que recomeçar ... ").ConfigureAwait(false);

				// Send a trace activity, which will be displayed in the Bot Framework Emulator
				await turnContext.TraceActivityAsync("OnTurnError Trace", exception.Message, "https://www.botframework.com/schemas/error", "TurnError").ConfigureAwait(false);

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
	}

}
