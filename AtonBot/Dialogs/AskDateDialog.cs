// AtonBot
// AskDateDialog
//
// 1- Pergunta qual data deseja, dentro das datas disponíveis
// 2- Se digitou 'Outra', pergunta a data
// 3- Salva a data escolhida, e volta

using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using MrBot.Data;
using MrBot.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using PloomesApi;

namespace MrBot.Dialogs
{
	public class AskDateDialog: ComponentDialog
	{
		// Dicionario de frases e ícones 
		private readonly DialogDictionary _dialogDictionary;
		private readonly BotDbContext _botDbContext;
		private readonly ConversationState _conversationState;
		private readonly PloomesClient _ploomesclient;
		private readonly Customer _customer;
		private readonly Deal _deal;

		public AskDateDialog(BotDbContext botContext, DialogDictionary dialogDictionary, ConversationState conversationState, IBotTelemetryClient telemetryClient, PloomesClient ploomesClient, Customer customer, Deal deal)
			: base(nameof(AskDateDialog))
		{

			// Injected objects
			_dialogDictionary = dialogDictionary;
			_botDbContext = botContext;
			_conversationState = conversationState;
			_ploomesclient = ploomesClient;
			_customer = customer;
			_deal = deal;

			// Set the telemetry client for this and all child dialogs.
			this.TelemetryClient = telemetryClient;

			// Adiciona um diálogo de prompt de texto com validação das datas
			AddDialog(new TextPrompt("dateprompt", DateValidatorAsync));
			// Adiciona um diálogo de prompt de texto com validação de outra dat
			AddDialog(new TextPrompt("OtherDatePrompt", OtherDateValidatorAsync));

			// Adiciona um dialogo WaterFall com os passos
			AddDialog(new WaterfallDialog(nameof(WaterfallDialog), new WaterfallStep[]
			{
				AskDateStepAsync,
				AskOtherDateOrReturnStepAsync,
				EndStepAsync
			}));

			// Configura para iniciar no WaterFall Dialog
			InitialDialogId = nameof(WaterfallDialog);
		}

		// Consulta opções de datas com base no CEP, oferece opçoes, pergunta data
		private async Task<DialogTurnResult> AskDateStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{

			// Busca o CEP - que foi passado por parâmetro quando este diálogo foi chamado
			string cep = (string)stepContext.Options;

			// Confere se recebeu o cep
			if ( cep == null)
            {
				// Avisa que deu erro
				await stepContext.Context.SendActivityAsync(MessageFactory.Text("Me desculpe, ocorreu algum erro. Não recebi o seu CEP. Vou ter que recomeçar."), cancellationToken).ConfigureAwait(false);

				// Encera
				await stepContext.CancelAllDialogsAsync().ConfigureAwait(false);
				return await stepContext.EndDialogAsync().ConfigureAwait(false);
			}

			// Procura as opções de data com base no CEP informado
			List<DateTime> nextAvailableDates= GetNextAvailableDates(cep);

			// Salva as datas disponiveis em variavel persistente
			stepContext.Values["nextAvailableDates"] = nextAvailableDates;

			// Salva as datas disponíveis no conversationData - para poder se acessado na função de validação
			var conversationStateAccessors = _conversationState.CreateProperty<ConversationData>(nameof(ConversationData));
			var conversationData = await conversationStateAccessors.GetAsync(stepContext.Context, () => new ConversationData()).ConfigureAwait(false);
			conversationData.ResetAvailableDates();
			foreach ( DateTime availableDate in nextAvailableDates)
				conversationData.AddAvailableDate(availableDate);

			// Monta HeroCard para perguntar a data desejada, dentro das opções disponíveis
			var card = new HeroCard
			{
				Title = $"Agendamento {_dialogDictionary.Emoji.Calendar}",
				Text = "Obrigado pela informação. Para seu endereço temos as seguintes datas disponíveis:",
				Buttons = new List<CardAction> { }
			};

			// Adiciona botões para as datas disponíveis
			for ( int x = 0; x <= nextAvailableDates.Count-1; x++)
				card.Buttons.Add(new CardAction(ActionTypes.ImBack, title: nextAvailableDates[x].ToString("dd/MM", CultureInfo.InvariantCulture), value: nextAvailableDates[x]));


			// Adiciona botão para "outra data"
			card.Buttons.Add(new CardAction(ActionTypes.ImBack, title: "outra data", value: "outra"));

			// Send the card(s) to the user as an attachment to the activity
			await stepContext.Context.SendActivityAsync(MessageFactory.Attachment(card.ToAttachment()), cancellationToken).ConfigureAwait(false);

			// Aguarda uma resposta
			string retryText = "Por favor, escolha uma destas datas: ";
			foreach (DateTime nextavailabledate in nextAvailableDates)
				retryText += " " + nextavailabledate.ToString("dd/MM", CultureInfo.InvariantCulture);
			return await stepContext.PromptAsync("dateprompt", new PromptOptions { Prompt = null, RetryPrompt = MessageFactory.Text(retryText) }, cancellationToken).ConfigureAwait(false);

		}

		// Se digitou 'outra', pergunta a opção de data
		// Caso contrario, retorna com a data escolhida
		private async Task<DialogTurnResult> AskOtherDateOrReturnStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{

			// Busca o que foi digitado
			string input = (string)stepContext.Result;

			// Se quer outra data
			if (input.ToUpper().Contains("OUTRA"))
            {
				// Pega a primeira data disponivel
				List<DateTime> nextAvailableDates = (List<DateTime>)stepContext.Values["nextAvailableDates"];
				DateTime firstAvailableDate = nextAvailableDates[0];

				// Pergunta qual é a data desejada
				return await stepContext.PromptAsync("OtherDatePrompt", new PromptOptions { Prompt = MessageFactory.Text($"Tudo bem! Por favor, me informe para qual data você deseja. Digite no formato dia/mês: 📌"), RetryPrompt = MessageFactory.Text($"Por favor, informe uma data válida, no formato DD/MM.") }, cancellationToken).ConfigureAwait(false);

			}
			else
            {
				// Busca a data em formato string que informada no passo anterior
				string choice = ((string)stepContext.Result).PadLeft(2, '0');

				// Ponteiro para os dados persistentes da conversa
				var conversationStateAccessors = _conversationState.CreateProperty<ConversationData>(nameof(ConversationData));
				var conversationData = await conversationStateAccessors.GetAsync(stepContext.Context, () => new ConversationData()).ConfigureAwait(false);

				// Substitui hifen por barra ( se digitar 15-05 vira 15/07 pra achar a data ), e retira palavra dia, caso tenha sido digitado
				choice = choice.Replace("-", "/").Replace("dia ", "");

				// Busca novamente as datas disponíveis
				List<DateTime> nextAvailableDates = conversationData.NextAvailableDates;

				// Varre as datas pra conferir com que data a string digitada de escolha confere
				foreach (DateTime data in nextAvailableDates)
				{
					if (choice == data.ToString("dd/MM", CultureInfo.InvariantCulture) | choice == data.ToString("dd/MM", CultureInfo.InvariantCulture).Split("/")[0])
					{
						// Finaliza o diálogo, e retorna a data que foi escolhida
						return await stepContext.EndDialogAsync(data.ToString("dd/MM", CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
					}
				}

				// Pula pro proximo passo
				return await stepContext.NextAsync().ConfigureAwait(false);
			}

		}

		// Finaliza e devolve a data escolhida
		private async Task<DialogTurnResult> EndStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{
			// Busca o que foi digitado
			string data = (string)stepContext.Result;

			// Finaliza o diálogo, e retorna a data que foi escolhida
			return await stepContext.EndDialogAsync(data, cancellationToken).ConfigureAwait(false);

		}

		// Valida as datas - se o cliente digitou uma data dentro do array de datas disponiveis
		private async Task<bool> DateValidatorAsync(PromptValidatorContext<string> promptContext, CancellationToken cancellationToken)
		{

			// Busca o que foi digitado
			string choice = (string)promptContext.Context.Activity.Text;
			choice = choice.PadLeft(2, '0');

			// Substitui hifen por barra ( se digitar 15-05 vira 15/07 pra achar a data ), e retira palavra dia, caso tenha sido digitado
			choice = choice.Replace("-", "/").Replace("dia ", "");

			// Ponteiro para os dados persistentes da conversa
			var conversationStateAccessors = _conversationState.CreateProperty<ConversationData>(nameof(ConversationData));
			var conversationData = await conversationStateAccessors.GetAsync(promptContext.Context, () => new ConversationData()).ConfigureAwait(false);

			// Busca novamente as datas disponíveis
			List<DateTime> nextAvailableDates = conversationData.NextAvailableDates;
			// Array com as escolhas em format string dd/MM
			List<string> validchoices= new List<string>();

			// Adiciona as datas e os dia das datas as possibilidaes de validação- pra validar se o cliente digitar somente o dia
			foreach (DateTime data in nextAvailableDates)
			{
				validchoices.Add(data.ToString("dd/MM", CultureInfo.InvariantCulture));
				validchoices.Add(data.ToString("dd/MM", CultureInfo.InvariantCulture).Split("/")[0]);
			}

			// Devolve true or false se a escolha esta dentro da lista de datas disponíveis
			return validchoices.Contains(choice) | choice.ToUpper().Contains("OUTRA");
		}

		// Valida  se o cliente escolheu outra data ( fora da lista oferecida )
		// Tem que ser dia util, apos a primeira data disponível
		private async Task<bool> OtherDateValidatorAsync(PromptValidatorContext<string> promptContext, CancellationToken cancellationToken)
		{

			// Busca o que foi digitado
			string choice = (string)promptContext.Context.Activity.Text;
			choice = choice.PadLeft(2, '0');

			// Substitui hifen por barra ( se digitar 15-05 vira 15/07 pra achar a data ), e retira palavra dia, caso tenha sido digitado
			choice = choice.Replace("-", "/").Replace("dia ", "");

			// Verifica se o que foi digitado é uma data válida
			if (DateTime.TryParse(choice, new CultureInfo("pt-BR"), DateTimeStyles.None, out DateTime dataescolhida))
			{
				// Ponteiro para os dados persistentes da conversa
				var conversationStateAccessors = _conversationState.CreateProperty<ConversationData>(nameof(ConversationData));
				var conversationData = await conversationStateAccessors.GetAsync(promptContext.Context, () => new ConversationData()).ConfigureAwait(false);

				// Busca novamente as datas disponíveis
				List<DateTime> nextAvailableDates = conversationData.NextAvailableDates;

				// Confere qual é a primeira data disponível
				DateTime firstAvailableDate = nextAvailableDates[0];

				// Confere se a data escolhida é igual ou depois da primeira data disponível, e se é dia util
				if (DateTime.Compare(firstAvailableDate, dataescolhida) <= 0 & !Utility.IsHoliday(dataescolhida) & !Utility.IsWeekend(dataescolhida))
				{
					// Adiciona a data escolhida, a lista de datas disponíveis ( porque vai validar no outro diálogo )
					conversationData.AddAvailableDate(dataescolhida);
					return true;
				}
				else
				{
					if (DateTime.Compare(firstAvailableDate, dataescolhida) > 0)
						await promptContext.Context.SendActivityAsync(MessageFactory.Text($"Só temos agenda disponível a partir do dia {firstAvailableDate:dd/MM}. \nPor favor, escolha outra data. 🔖"), cancellationToken).ConfigureAwait(false);
					else if (Utility.IsHoliday(dataescolhida))
						await promptContext.Context.SendActivityAsync(MessageFactory.Text($"Dia {dataescolhida:dd/MM} é feriado! 🎉 \nPor favor, escolha outra data."), cancellationToken).ConfigureAwait(false);
					else if (Utility.IsWeekend(dataescolhida))
						await promptContext.Context.SendActivityAsync(MessageFactory.Text($"Dia {dataescolhida:dd/MM} cai no final de semana! 🥂 \nPor favor, escolha outra data."), cancellationToken).ConfigureAwait(false);
					return false;
				}
			}
			else
				return false;

		}


		// Busca as próximas datas disponiveis, com base no CEP informado
		private static List<DateTime> GetNextAvailableDates(string cep)
		{
			List<DateTime> nextAvailableDates = new List<DateTime>();

			//TODO: Lógica para obter as próximas datas disponíveis
			int choicesQuantity = 3;
			int nextDateDelay = 7;
			int icep = int.Parse(Utility.ClearStringNumber(cep));
			if ( Utility.CepIsCapital(icep) )
			{
				nextDateDelay = 5;
			}
			DateTime nextDate = DateTime.Today.AddDays(nextDateDelay+1); 

			do
			{
				nextDate = Utility.GetNextWorkingDay(nextDate);
				nextAvailableDates.Add(nextDate);

			} while (nextAvailableDates.Count < choicesQuantity);

			return nextAvailableDates;
		}
	}
}