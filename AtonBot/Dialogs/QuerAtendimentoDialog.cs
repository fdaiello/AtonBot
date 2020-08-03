// MrBot 2020
//
// QuerTestarDialog
// Chamado por smsInfoDialogo
//
//    1- Pergunta se quer falar com um atendente
//    2- Se respondeu que sim
//          chama CallHumanDialog
//       Se rspondeu que nao
//          da mensagem e encerra o diálogo

using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MrBot.Dialogs
{
	public class QuerAtendimentoDialog: ComponentDialog
	{
		// Dicionario de frases e ícones 
		private readonly DialogDictionary _dialogDictionary;

		public QuerAtendimentoDialog(DialogDictionary dialogDictionary, IBotTelemetryClient telemetryClient, CallHumanDialog callHumanDialog)
			: base(nameof(QuerAtendimentoDialog))
		{

			// Injected objects
			_dialogDictionary = dialogDictionary;

			// Set the telemetry client for this and all child dialogs.
			this.TelemetryClient = telemetryClient;

			// Adiciona o diálogos filhos a este conjunto de diálogos
			AddDialog(callHumanDialog);

			// Adiciona um diálogo de prompt de texto para continuar
			AddDialog(new TextPrompt("sim_nao", YesNoValidatorAsync));

			// Adiciona um diálogo de prompt de texto para continuar
			AddDialog(new TextPrompt("Continuar"));

			// Adiciona um dialogo WaterFall com os 2 passos: Mostra as opções, Executa as opções
			AddDialog(new WaterfallDialog(nameof(WaterfallDialog), new WaterfallStep[]
			{
				AskQuerAtendimentoStepAsync,
				QuerAtendimentoStep1Async
			}));

			// Configura para iniciar no WaterFall Dialog
			InitialDialogId = nameof(WaterfallDialog);
		}

		// Pergunta se já quer atendimento humano
		private async Task<DialogTurnResult> AskQuerAtendimentoStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{

			// Create a HeroCard with options for the user to interact with the bot.
			var card = new HeroCard
			{
				Text = "Você quer que eu chame um atendente pra falar com você? " + _dialogDictionary.Emoji.Person,
				Buttons = new List<CardAction>
				{
					new CardAction(ActionTypes.ImBack, title: "Sim", value: "sim"),
					new CardAction(ActionTypes.ImBack, title: "Não", value: "não"),
				},
			};

			// Send the card(s) to the user as an attachment to the activity
			await stepContext.Context.SendActivityAsync(MessageFactory.Attachment(card.ToAttachment()), cancellationToken).ConfigureAwait(false);

			// Aguarda uma resposta
			return await stepContext.PromptAsync("sim_nao", new PromptOptions { Prompt = null, RetryPrompt = MessageFactory.Text("Por favor, digite: Sim ou Não") }, cancellationToken).ConfigureAwait(false);
		}

		// Verifica se digitou sim ou não para a pergunta "quer atendimento humano" ....
		private async Task<DialogTurnResult> QuerAtendimentoStep1Async(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{
			// Busca a opção informada no passo anterior
			string choice = ((string)stepContext.Result).ToLower();
			// Salva a opção
			stepContext.Values["choice"] = choice;

			// Se dise que sim
			if ( choice == "sim")
				// Chama diálogo pra ver se tem conta, ou criar uma
				return await stepContext.BeginDialogAsync(nameof(CallHumanDialog), null, cancellationToken).ConfigureAwait(false);

			// Se disse que não
			else
			{
				// Envia a mensagem explicativa
				string message = "Ok, quando precisar, estarei a disposição.";
				await stepContext.Context.SendActivityAsync(MessageFactory.Text(message), cancellationToken).ConfigureAwait(false);

				// E encerra
				return await stepContext.EndDialogAsync(null, cancellationToken).ConfigureAwait(false);

			}
		}

		// Validação: Sim ou Nâo
		private async Task<bool> YesNoValidatorAsync(PromptValidatorContext<string> promptContext, CancellationToken cancellationToken)
		{
			bool IsValid;

			// Verifica se o que o cliente digitou uma escolha de 1 a 2
			string choice = promptContext.Context.Activity.Text.ToLower();
			IsValid = choice == "sim" || choice == "não" || choice == "nao";

			// retorna
			return await Task.FromResult(IsValid).ConfigureAwait(false);
		}
	}
}
