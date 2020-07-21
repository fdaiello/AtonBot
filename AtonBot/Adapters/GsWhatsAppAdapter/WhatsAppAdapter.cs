// Adaptar para API do WhatsApp pela GupShup
// Tomado como modelo o projeto do GsWhatsApp Adaptar
// https://github.com/microsoft/botbuilder-dotnet/tree/master/libraries/Adapters
// felipedaiello@gmail.com - 10/05/2020

using Microsoft.AspNetCore.Http;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GsWhatsApp;

namespace GsWhatsAppAdapter
{
	/// <summary>
	/// A <see cref="BotAdapter"/> that can connect to WhatsApp Businnes API provided by GupShup https://www.gupshup.io/developer/home.
	/// </summary>
	public class WhatsAppAdapter : BotAdapter, IBotFrameworkHttpAdapter
	{
		private readonly WhatsAppHelper whatsAppHelper;
		private readonly GsWhatsAppClient gsWhatsAppClient;
		private readonly SpeechClient speechClient;
		private readonly ILogger logger;

		/// <summary>
		/// Initializes a new instance of the <see cref="WhatsAppAdapter"/> class using configuration settings.
		/// </summary>
		/// <param name="configuration">An <see cref="IConfiguration"/> instance.</param>
		/// <remarks>
		/// The configuration keys are:
		/// GsApiKey: The API KEY from the account at GupShup.IO associated with WhatsApp number
		/// GsApiUri: URI for API calls
		/// GsMediaUri: URI for retreaving media
		/// </remarks>
		/// <param name="logger">The ILogger implementation this adapter should use.</param>
		public WhatsAppAdapter(IConfiguration configuration, ILogger logger)
		{
			if (configuration == null)
				throw new ArgumentNullException(nameof(configuration));

			gsWhatsAppClient = new GsWhatsAppClient(new GsWhatsAppOptions(configuration["GsSettings:GsApiKey"], new Uri(configuration["GsSettings:GsApiUri"]), new Uri(configuration["GsSettings:GsMediaUri"])));
			speechClient = new SpeechClient(new SpeechOptions(configuration["SpeechSubcriptionKey"], configuration["SpeechRegion"], configuration["Language"]), logger);
			whatsAppHelper = new WhatsAppHelper(gsWhatsAppClient, speechClient, new Uri(configuration["BotUrl"]), configuration);
			this.logger = logger;

			// Confere se o diretorio bin foi adicionado ao PATH do ambiente: neceesario pro IIS achar o libmp3lame.32.dll
			CheckAddDllPath();
		}

		/// <summary>
		/// Sends activities to the conversation.
		/// </summary>
		/// <param name="turnContext">The context object for the turn.</param>
		/// <param name="activities">The activities to send.</param>
		/// <param name="cancellationToken">A cancellation token that can be used by other objects
		/// or threads to receive notice of cancellation.</param>
		/// <returns>A task that represents the work queued to execute.</returns>
		/// <remarks>If the activities are successfully sent, the task result contains
		/// an array of <see cref="ResourceResponse"/> objects containing the SIDs that
		/// GsWhatsApp assigned to the activities.</remarks>
		/// <seealso cref="ITurnContext.OnSendActivities(SendActivitiesHandler)"/>
		public override async Task<ResourceResponse[]> SendActivitiesAsync(ITurnContext turnContext, Activity[] activities, CancellationToken cancellationToken)
		{

			var responses = new List<ResourceResponse>();
			foreach (var activity in activities)
			{
				if (activity.Type != ActivityTypes.Message)
				{
					logger.LogDebug($"Unsupported Activity Type: '{activity.Type}'. Only Activities of type 'Message' are supported.");
				}
				else
				{

					var res = await whatsAppHelper.SendActivityToWhatsApp(activity).ConfigureAwait(false);

					var response = new ResourceResponse() { Id = res, };

					responses.Add(response);
				}

			}

			return responses.ToArray();
		}

		/// <summary>
		/// Creates a turn context and runs the middleware pipeline for an incoming activity.
		/// </summary>
		/// <param name="httpRequest">The incoming HTTP request.</param>
		/// <param name="httpResponse">When this method completes, the HTTP response to send.</param>
		/// <param name="bot">The bot that will handle the incoming activity.</param>
		/// <param name="cancellationToken">A cancellation token that can be used by other objects
		/// or threads to receive notice of cancellation.</param>
		/// <returns>A task that represents the work queued to execute.</returns>
		/// <exception cref="ArgumentNullException"><paramref name="httpRequest"/>,
		/// <paramref name="httpResponse"/>, or <paramref name="bot"/> is <c>null</c>.</exception>
		public async Task ProcessAsync(HttpRequest httpRequest, HttpResponse httpResponse, IBot bot, CancellationToken cancellationToken)
		{
			if (httpRequest == null)
				throw new ArgumentNullException(nameof(httpRequest));

			if (httpResponse == null)
				throw new ArgumentNullException(nameof(httpResponse));

			if (bot == null)
				throw new ArgumentNullException(nameof(bot));

			// cria uma activity com base no payload enviado na requisição
			var activity = await whatsAppHelper.PayloadToActivity(httpRequest, logger).ConfigureAwait(false);

			// se não retornou activity valida
			if (activity == null )
				// Devolve resposta em branco: Ok
				await whatsAppHelper.WriteOkAsync(httpResponse, cancellationToken).ConfigureAwait(false);

			else
			{
				// create a conversation reference
				using (var context = new TurnContext(this, activity))
				{
					context.TurnState.Add("httpStatus", HttpStatusCode.OK.ToString("D"));
					await RunPipelineAsync(context, bot.OnTurnAsync, cancellationToken).ConfigureAwait(false);

					var statusCode = Convert.ToInt32(context.TurnState.Get<string>("httpStatus"), CultureInfo.InvariantCulture);
					var text = context.TurnState.Get<object>("httpBody") != null ? context.TurnState.Get<object>("httpBody").ToString() : string.Empty;

					await whatsAppHelper.WriteAsync(httpResponse, statusCode, text, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
				}

			}

		}

		/// <summary>
		/// Replaces an existing activity in the conversation.
		/// GsWhatsApp SMS does not support this operation.
		/// </summary>
		/// <param name="turnContext">The context object for the turn.</param>
		/// <param name="activity">New replacement activity.</param>
		/// <param name="cancellationToken">A cancellation token that can be used by other objects
		/// or threads to receive notice of cancellation.</param>
		/// <returns>A task that represents the work queued to execute.</returns>
		/// <remarks>This method always returns a faulted task.</remarks>
		/// <seealso cref="ITurnContext.OnUpdateActivity(UpdateActivityHandler)"/>
		public override Task<ResourceResponse> UpdateActivityAsync(ITurnContext turnContext, Activity activity, CancellationToken cancellationToken)
		{
			return Task.FromException<ResourceResponse>(new NotSupportedException("WhatsApp does not support updating activities."));
		}

		/// <summary>
		/// Deletes an existing activity in the conversation.
		/// GsWhatsApp SMS does not support this operation.
		/// </summary>
		/// <param name="turnContext">The context object for the turn.</param>
		/// <param name="reference">Conversation reference for the activity to delete.</param>
		/// <param name="cancellationToken">A cancellation token that can be used by other objects
		/// or threads to receive notice of cancellation.</param>
		/// <returns>A task that represents the work queued to execute.</returns>
		/// <remarks>This method always returns a faulted task.</remarks>
		/// <seealso cref="ITurnContext.OnDeleteActivity(DeleteActivityHandler)"/>
		public override Task DeleteActivityAsync(ITurnContext turnContext, ConversationReference reference, CancellationToken cancellationToken)
		{
			return Task.FromException<ResourceResponse>(new NotSupportedException("WhatsApp does not support deleting activities."));
		}

		/// <summary>
		/// Sends a proactive message to a conversation.
		/// </summary>
		/// <param name="reference">A reference to the conversation to continue.</param>
		/// <param name="logic">The method to call for the resulting bot turn.</param>
		/// <param name="cancellationToken">A cancellation token that can be used by other objects
		/// or threads to receive notice of cancellation.</param>
		/// <returns>A task that represents the work queued to execute.</returns>
		/// <remarks>Call this method to proactively send a message to a conversation.
		/// Most channels require a user to initiate a conversation with a bot
		/// before the bot can send activities to the user.</remarks>
		/// <seealso cref="BotAdapter.RunPipelineAsync(ITurnContext, BotCallbackHandler, CancellationToken)"/>
		/// <exception cref="ArgumentNullException"><paramref name="reference"/> or
		/// <paramref name="logic"/> is <c>null</c>.</exception>
		public async Task ContinueConversationAsync(ConversationReference reference, BotCallbackHandler logic, CancellationToken cancellationToken)
		{
			if (reference == null)
				throw new ArgumentNullException(nameof(reference));

			if (logic == null)
				throw new ArgumentNullException(nameof(logic));

			var request = reference.GetContinuationActivity().ApplyConversationReference(reference, true);

			using (var context = new TurnContext(this, request))
			{
				await RunPipelineAsync(context, logic, cancellationToken).ConfigureAwait(false);
			}
		}

		/// <summary>
		/// Sends a proactive message from the bot to a conversation.
		/// </summary>
		/// <param name="claimsIdentity">A <see cref="ClaimsIdentity"/> for the conversation.</param>
		/// <param name="reference">A reference to the conversation to continue.</param>
		/// <param name="callback">The method to call for the resulting bot turn.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>A task that represents the work queued to execute.</returns>
		/// <remarks>Call this method to proactively send a message to a conversation.
		/// Most _channels require a user to initialize a conversation with a bot
		/// before the bot can send activities to the user.
		/// <para>This method registers the following services for the turn.<list type="bullet">
		/// <item><description><see cref="IIdentity"/> (key = "BotIdentity"), a claims claimsIdentity for the bot.
		/// </description></item>
		/// </list></para>
		/// </remarks>
		/// <seealso cref="BotAdapter.RunPipelineAsync(ITurnContext, BotCallbackHandler, CancellationToken)"/>
		public override async Task ContinueConversationAsync(ClaimsIdentity claimsIdentity, ConversationReference reference, BotCallbackHandler callback, CancellationToken cancellationToken)
		{
			if (reference == null)
				throw new ArgumentNullException(nameof(reference));

			using (var context = new TurnContext(this, reference.GetContinuationActivity()))
			{
				context.TurnState.Add<IIdentity>(BotIdentityKey, claimsIdentity);
				context.TurnState.Add<BotCallbackHandler>(callback);
				await RunPipelineAsync(context, callback, cancellationToken).ConfigureAwait(false);
			}
		}
		// Confere se o diretorio bin foi adicionado ao PATH do ambiente: neceesario pro IIS achar o libmp3lame.32.dll
		public static void CheckAddDllPath()
		{
			// find path to base directory
			var dllPath = AppDomain.CurrentDomain.BaseDirectory;
			// get current search path from environment
			var path = Environment.GetEnvironmentVariable("PATH") ?? "";

			// add 'bin' folder to search path if not already present
			if (!path.Split(Path.PathSeparator).Contains(dllPath, StringComparer.CurrentCultureIgnoreCase))
			{
				path = string.Join(Path.PathSeparator.ToString(new CultureInfo("en-US")), new string[] { path, dllPath });
				Environment.SetEnvironmentVariable("PATH", path);
			}
		}
	}
}
