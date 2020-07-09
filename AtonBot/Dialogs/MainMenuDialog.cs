// MrBot 2020
// Menu Principal

using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Microsoft.Bot.Builder.AI.QnA;
using MrBot.Data;
using MrBot.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MrBot.CognitiveModels;

namespace MrBot.Dialogs
{
	public class MainMenuDialog : ComponentDialog
	{

		// Dicionario de frases e ícones 
		private readonly DialogDictionary _dialogDictionary;

		// Contexto de dados do Bot
		private readonly BotDbContext _botDbContext;

		// Conversation persistent data
		private readonly ConversationState _conversationState;

		public MainMenuDialog(BotDbContext botContext, DialogDictionary dialogDictionary, CallHumanDialog callHumanDialog, QuerAtendimentoDialog querAtendimentoDialog, IBotTelemetryClient telemetryClient, ConversationState conversationState)
			: base(nameof(MainMenuDialog))
		{

			// Injected objects
			_botDbContext = botContext;
			_dialogDictionary = dialogDictionary;
			_conversationState = conversationState;

			// Set the telemetry client for this and all child dialogs.
			this.TelemetryClient = telemetryClient;

			// Adiciona os subdialogos que este diálogo vai usar
			AddDialog(callHumanDialog);
			AddDialog(querAtendimentoDialog);

			// Adiciona um diálogo de prompt de texto- para ler as opções do Menu
			AddDialog(new TextPrompt("MainMenuPrompt"));

			// Adiciona um diálogo WaterFall com os passos ( métodos ) que serão executados.
			AddDialog(new WaterfallDialog(nameof(WaterfallDialog), new WaterfallStep[]
			{
				ShowMainMenuStepAsync,
				CallMenuOptionStepAsync
			}));

			// Configura este diálogo para iniciar rodando o WatefallDialog criado acima.
			InitialDialogId = nameof(WaterfallDialog);
		}

		// Menu Principal
		private async Task<DialogTurnResult> ShowMainMenuStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{
			// Create a HeroCard with options for the user to interact with the bot.
			var card = new HeroCard
			{
				Title = "Menu",
				Text = $"{_dialogDictionary.Emoji.Smilingfacewithsmilingeyes} Estes são os comandos que eu sei responder:\n",
				Buttons = new List<CardAction>
				{
					new CardAction(ActionTypes.ImBack, title: $"Informações", value: "Informações"),
					new CardAction(ActionTypes.ImBack, title: $"Falar com um atendente", value: "Falar com um atendente"),
				},
			};

			// Text do hero card exclusivo pro WhatsApp
			if (stepContext.Context.Activity.ChannelId == "whatsapp")
				card.Text += $"Digite ou envie áudio{_dialogDictionary.Emoji.ExclamationMark}";

			// Send the card(s) to the user as an attachment to the activity
			await stepContext.Context.SendActivityAsync(MessageFactory.Attachment(card.ToAttachment()), cancellationToken).ConfigureAwait(false);

			// Aguarda uma resposta
			return await stepContext.PromptAsync("MainMenuPrompt", new PromptOptions { Prompt = null }, cancellationToken).ConfigureAwait(false);
		}

		// Roda no proximo turno, depois que o cliente digitou uma opção - e Executa a opção desejada
		private async Task<DialogTurnResult> CallMenuOptionStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{

			// Busca a opção informada no passo anterior - Customer Menu
			string choice = ((string)stepContext.Result).ToLower();

			if (choice.Contains("informacoes") | choice.Contains("informações") )
			{
				// E chama o QnA
				return await CallQnaDialog(stepContext, cancellationToken).ConfigureAwait(false);
			}
			else if (choice.Contains("atendente") )
				// Call Falar com um Atendente
				return await stepContext.BeginDialogAsync(nameof(CallHumanDialog), null, cancellationToken).ConfigureAwait(false);

			else
			{
				// Não entendeu ....
				string repromptMsg = $"Desculpe, não entendi.{_dialogDictionary.Emoji.DisapointedFace} Diga em poucas palavras o que você precisa.";
				await stepContext.Context.SendActivityAsync(MessageFactory.Text(repromptMsg), cancellationToken).ConfigureAwait(false);

				// Ponteiro para os dados persistentes da conversa
				var conversationStateAccessors = _conversationState.CreateProperty<ConversationData>(nameof(ConversationData));
				var conversationData = await conversationStateAccessors.GetAsync(stepContext.Context, () => new ConversationData()).ConfigureAwait(false);

				// Incrementa o contador de falhas de entendimento da intenção
				conversationData.IntentNotRecognized += 1;

				// Se ja errou 2 ou mais vezes
				if ( conversationData.IntentNotRecognized > 1)
					// Pergunta se quer falar com um atendente
					return await stepContext.BeginDialogAsync(nameof(QuerAtendimentoDialog), null, cancellationToken).ConfigureAwait(false);
				else
					// Fica em Loop neste Menu: MainMenuDialog
					return await stepContext.ReplaceDialogAsync(InitialDialogId, null, cancellationToken).ConfigureAwait(false);
			}

		}


		private static async Task<DialogTurnResult> CallQnaDialog(DialogContext innerDc, CancellationToken cancellationToken)
		{
			// Set values for generate answer options.
			var qnamakerOptions = new QnAMakerOptions
			{
				ScoreThreshold = QnAMakerMultiturnDialog.DefaultThreshold,
				Top = QnAMakerMultiturnDialog.DefaultTopN,
				Context = new QnARequestContext()
			};

			var noAnswer = (Activity)Activity.CreateMessageActivity();
			noAnswer.Text = QnAMakerMultiturnDialog.DefaultNoAnswer;

			var cardNoMatchResponse = new Activity(QnAMakerMultiturnDialog.DefaultCardNoMatchResponse);

			// Set values for dialog responses.	
			var qnaDialogResponseOptions = new QnADialogResponseOptions
			{
				NoAnswer = noAnswer,
				ActiveLearningCardTitle = QnAMakerMultiturnDialog.DefaultCardTitle,
				CardNoMatchText = QnAMakerMultiturnDialog.DefaultCardNoMatchText,
				CardNoMatchResponse = cardNoMatchResponse
			};

			var dialogOptions = new Dictionary<string, object>
			{
				[QnAMakerMultiturnDialog.QnAOptions] = qnamakerOptions,
				[QnAMakerMultiturnDialog.QnADialogResponseOptions] = qnaDialogResponseOptions
			};

			return await innerDc.BeginDialogAsync(nameof(QnAMakerMultiturnDialog), dialogOptions, cancellationToken).ConfigureAwait(false);
		}
	}
}
