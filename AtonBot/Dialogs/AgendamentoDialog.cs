// AtonBot
// AgendamentoDialog
//
// 1- Quer agendar?
// 2- Pergunta CEP
// 3- Consulta opções de datas, oferece opçoes, pergunta data
// 4- Pergunta o turno
// 5- Salva o Lead no Ploomes, confirma agendamento

using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace MrBot.Dialogs
{
	public class AgendamentoDialog: ComponentDialog
	{
		// Dicionario de frases e ícones 
		private readonly DialogDictionary _dialogDictionary;

		public AgendamentoDialog(DialogDictionary dialogDictionary, IBotTelemetryClient telemetryClient)
			: base(nameof(AgendamentoDialog))
		{

			// Injected objects
			_dialogDictionary = dialogDictionary;

			// Set the telemetry client for this and all child dialogs.
			this.TelemetryClient = telemetryClient;

			// Adiciona um diálogo de prompt de texto para continuar
			AddDialog(new TextPrompt("sim_nao", YesNoValidatorAsync));
			// Adiciona um diálogo de prompt de texto sem validação
			AddDialog(new TextPrompt(nameof(TextPrompt)));
			// Adiciona um diálogo de prompt de texto sem validação
			AddDialog(new TextPrompt("turnoprompt",TurnoValidatorAsync));
			// Adiciona um diálogo de texto com validaçao de CEP
			AddDialog(new TextPrompt("CepPrompt", CEPValidatorAsync));

			// Adiciona um dialogo WaterFall com os 2 passos: Mostra as opções, Executa as opções
			AddDialog(new WaterfallDialog(nameof(WaterfallDialog), new WaterfallStep[]
			{
				AskQuerAgendarStepAsync,
				AskCepStepAsync,
				AskDateStepAsync,
				AskTimeStepAsync,
			}));

			// Configura para iniciar no WaterFall Dialog
			InitialDialogId = nameof(WaterfallDialog);
		}

		// 1- Quer agendar?
		//   Gostaria de agendar uma visita técnica para realizar a instalação em sua residência?
		private async Task<DialogTurnResult> AskQuerAgendarStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{

			// Create a HeroCard with options for the user to interact with the bot.
			var card = new HeroCard
			{
				Text = "Você gostaria de agendar uma visita técnica para realizar a instalação em sua residência? " + _dialogDictionary.Emoji.Person,
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

		// 2- Pergunta CEP
		// Verifica se digitou sim ou não para a pergunta "quer agendar"
		// Se sim, pergunta o CEP
		// Se não, se despede, e encerra
		private async Task<DialogTurnResult> AskCepStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{
			// Busca a opção informada no passo anterior
			string choice = ((string)stepContext.Result).ToLower();

			// Salva a opção
			stepContext.Values["choice"] = choice;

			// Se dise que sim
			if ( choice == "sim")
				// Pergunta o CEP
				return await stepContext.PromptAsync("CepPrompt", new PromptOptions { Prompt = MessageFactory.Text("Ótimo. Poderia nos informar por favor o cep da sua residência para checarmos a disponibilidae do técnico na sua região?"), RetryPrompt = MessageFactory.Text("Este não é um Cep válido. Por favor, digite novamente no formato 00000-000") }, cancellationToken).ConfigureAwait(false);

			// Se disse que não
			else
			{
				// Envia a mensagem explicativa
				string message = "Tudo bem. Se quiser voltar outra hora, estarei a sua disposição.";
				await stepContext.Context.SendActivityAsync(MessageFactory.Text(message), cancellationToken).ConfigureAwait(false);

				// E encerra
				return await stepContext.EndDialogAsync(null, cancellationToken).ConfigureAwait(false);

			}
		}

		// 3- Consulta opções de datas com base no CEP, oferece opçoes, pergunta data
		private async Task<DialogTurnResult> AskDateStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{
			// Busca o cep informado no passo anterior
			string cep = ((string)stepContext.Result).ToLower();

			// Salva o cep em variavel persistente do diálogo
			stepContext.Values["cep"] = cep;

			// Procura as opções de data com base no CEP informado
			List<string> nextAvailableDates= GetNextAvailableDates(cep);

			// Monta HeroCard para perguntar a data desejada, dentro das opções disponíveis
			var card = new HeroCard
			{
				Title = $"Agendamento {_dialogDictionary.Emoji.Calendar}",
				Text = "Obrigado pela informação. Para seu endereço temos as seguintes datas disponíveis:",
				Buttons = new List<CardAction> { }
			};
			// Adiciona botões para as datas disponíveis
			for ( int x = 0; x <= nextAvailableDates.Count; x++)
				card.Buttons.Add(new CardAction(ActionTypes.ImBack, title: nextAvailableDates[x], value: nextAvailableDates[x]));
			
			// Send the card(s) to the user as an attachment to the activity
			await stepContext.Context.SendActivityAsync(MessageFactory.Attachment(card.ToAttachment()), cancellationToken).ConfigureAwait(false);

			// Aguarda uma resposta
			return await stepContext.PromptAsync(nameof(TextPrompt), new PromptOptions { Prompt = null }, cancellationToken).ConfigureAwait(false);

		}
		// 4- Pergunta o turno
		private async Task<DialogTurnResult> AskTimeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{
			// Busca a data informada no passo anterior
			string data = ((string)stepContext.Result).ToLower();

			// Salva a data em varivel persitente ao diálogo
			stepContext.Values["data"] = data;

			// Pergunta o Turno desejado
			var card = new HeroCard
			{
				Title = $"Turno {_dialogDictionary.Emoji.AlarmClock}",
				Text = "Você prefere atendimento no período da manhã (08h as 13h) ou da tarde ( 13h às 18h)?",
				Buttons = new List<CardAction>
				{
					new CardAction(ActionTypes.ImBack, title: $"manhã", value: "manhã"),
					new CardAction(ActionTypes.ImBack, title: $"tarde", value: "tarde"),
				},
			};

			// Text do hero card exclusivo pro WhatsApp
			if (stepContext.Context.Activity.ChannelId == "whatsapp")
				card.Text += $"Digite ou envie áudio{_dialogDictionary.Emoji.ExclamationMark}";

			// Send the card(s) to the user as an attachment to the activity
			await stepContext.Context.SendActivityAsync(MessageFactory.Attachment(card.ToAttachment()), cancellationToken).ConfigureAwait(false);

			// Aguarda uma resposta
			return await stepContext.PromptAsync("turnoprompt", new PromptOptions { Prompt = null, RetryPrompt = MessageFactory.Text("Por favor, digite: manhã ou tarde") }, cancellationToken).ConfigureAwait(false);

		}
		// 5- Salva os dados no banco, salva o Lead no Ploomes, e confirma o agendamento
		private async Task<DialogTurnResult> SaveStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{
			// Busca o turno
			string turno = ((string)stepContext.Result).ToLower();

			// Salva o turno em varivel persitente ao diálogo
			stepContext.Values["turno"] = turno;

			// Responde para o usuário
			var msg = $"Ok! Obrigado. Sua visita técnica está agendada para o dia {stepContext.Values["data"]} no período da {stepContext.Values["turno"]}.\n48 horas antes do agendamento disponibilizaremos informações do técnico que fará a visita." + _dialogDictionary.Emoji.ThumbsUp;
			await stepContext.Context.SendActivityAsync(MessageFactory.Text(msg), cancellationToken).ConfigureAwait(false);

			// Termina este diálogo
			return await stepContext.EndDialogAsync().ConfigureAwait(false);

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
		// Validação: manhã ou tarde
		private async Task<bool> TurnoValidatorAsync(PromptValidatorContext<string> promptContext, CancellationToken cancellationToken)
		{
			bool IsValid;

			// Verifica se o que o cliente digitou manhã ou tarde
			string choice = promptContext.Context.Activity.Text.ToLower();
			IsValid = choice == "manhã" || choice == "manha" || choice == "tarde";

			// retorna
			return await Task.FromResult(IsValid).ConfigureAwait(false);
		}
		// Tarefa de validação do CEP
		private async Task<bool> CEPValidatorAsync(PromptValidatorContext<string> promptContext, CancellationToken cancellationToken)
		{
			// retorna
			return await Task.FromResult(Utility.IsValidCEP(Utility.FormataCEP(promptContext.Context.Activity.Text))).ConfigureAwait(false);
		}
		// Busca as próximas datas disponiveis, com base no CEP informado
		private static List<string> GetNextAvailableDates(string cep)
		{
			List<string> nextAvailableDates = new List<string>();

			//TODO: Lógica para obter as próximas datas disponíveis
			int choicesQuantity = 2;
			int nextDateDelay = 0;
			if (!cep.StartsWith("0"))
			{
				nextDateDelay = 1;
			}
			DateTime nextDate = DateTime.Today.AddDays(nextDateDelay); 

			do
			{
				nextDate = Utility.GetNextWorkingDay(nextDate);
				nextAvailableDates.Add(nextDate.ToString("dd/MM",CultureInfo.InvariantCulture));

			} while (nextAvailableDates.Count < choicesQuantity);

			return nextAvailableDates;
		}
	}
}
