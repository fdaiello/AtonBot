using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.AI.Luis;
using Microsoft.Extensions.Configuration;
using System.Threading;
using System.Threading.Tasks;

namespace MrBot.CognitiveModels
{
	public class MisterBotRecognizer : IRecognizer
	{

		private readonly LuisRecognizer _recognizer;

		public MisterBotRecognizer(IConfiguration configuration, IBotTelemetryClient telemetryClient)
		{
			var luisIsConfigured = !string.IsNullOrEmpty(configuration["Luis:AppId"]) && !string.IsNullOrEmpty(configuration["Luis:APIKey"]) && !string.IsNullOrEmpty(configuration["Luis:Endpoint"]);
			if (luisIsConfigured)
			{
				var luisApplication = new LuisApplication(
					configuration["Luis:AppId"],
					configuration["Luis:APIKey"],
					configuration["Luis:Endpoint"]);
				// Set the recognizer options depending on which endpoint version you want to use.
				// More details can be found in https://docs.microsoft.com/en-gb/azure/cognitive-services/luis/luis-migration-api-v3
				var recognizerOptions = new LuisRecognizerOptionsV3(luisApplication)
				{
					PredictionOptions = new Microsoft.Bot.Builder.AI.LuisV3.LuisPredictionOptions
					{
						IncludeInstanceData = true,
					},

					TelemetryClient = telemetryClient

				};

				_recognizer = new LuisRecognizer(recognizerOptions);

			}
		}

		// Returns true if luis is configured in the appsettings.json and initialized.
		public virtual bool IsConfigured => _recognizer != null;

		public virtual async Task<RecognizerResult> RecognizeAsync(ITurnContext turnContext, CancellationToken cancellationToken)
			=> await _recognizer.RecognizeAsync(turnContext, cancellationToken).ConfigureAwait(false);

		public virtual async Task<T> RecognizeAsync<T>(ITurnContext turnContext, CancellationToken cancellationToken)
			where T : IRecognizerConvert, new()
			=> await _recognizer.RecognizeAsync<T>(turnContext, cancellationToken).ConfigureAwait(false);
	}
}
