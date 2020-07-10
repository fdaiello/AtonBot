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
using MrBot.Data;
using MrBot.Models;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using PloomesApi;

using Newtonsoft.Json;

namespace MrBot.Dialogs
{
	public class AgendamentoDialog: ComponentDialog
	{
		// Dicionario de frases e ícones 
		private readonly DialogDictionary _dialogDictionary;
		private readonly BotDbContext _botDbContext;
		private readonly ConversationState _conversationState;
		private readonly PloomesClient _ploomesclient;

		public AgendamentoDialog(BotDbContext botContext, DialogDictionary dialogDictionary, ConversationState conversationState, IBotTelemetryClient telemetryClient, PloomesClient ploomesClient)
			: base(nameof(AgendamentoDialog))
		{

			// Injected objects
			_dialogDictionary = dialogDictionary;
			_botDbContext = botContext;
			_conversationState = conversationState;
			_ploomesclient = ploomesClient;

			// Set the telemetry client for this and all child dialogs.
			this.TelemetryClient = telemetryClient;

			// Adiciona um diálogo de prompt de texto para continuar
			AddDialog(new TextPrompt("sim_nao", YesNoValidatorAsync));
			// Adiciona um diálogo de prompt de texto com validação das datas
			AddDialog(new TextPrompt("dateprompt", DateValidatorAsync));
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
				SaveStepAsync
			}));

			// Configura para iniciar no WaterFall Dialog
			InitialDialogId = nameof(WaterfallDialog);
		}

		// 1- Quer agendar?
		//   Gostaria de agendar uma visita técnica para realizar a instalação em sua residência?
		private async Task<DialogTurnResult> AskQuerAgendarStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{
			// Initialize values
			stepContext.Values["name"] = string.Empty;
			stepContext.Values["phone"] = string.Empty;

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
				return await stepContext.PromptAsync("CepPrompt", new PromptOptions { Prompt = MessageFactory.Text($"Ótimo. Poderia nos informar por favor o cep {_dialogDictionary.Emoji.OpenMailBox} da sua residência para checarmos a disponibilidae do técnico na sua região?"), RetryPrompt = MessageFactory.Text("Este não é um Cep válido. Por favor, digite novamente no formato 00000-000") }, cancellationToken).ConfigureAwait(false);

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

			// Chama api dos Correios e salva cidade, bairro, estado
			GetAddressFromZip(stepContext, cep);

			// Procura as opções de data com base no CEP informado
			List<string> nextAvailableDates= GetNextAvailableDates(cep);

			// Salva as datas disponiveis em variavel persistente
			stepContext.Values["nextAvailableDates"] = nextAvailableDates;

			// Salva as datas disponíveis no conversationData - para poder se acessado na função de validação
			var conversationStateAccessors = _conversationState.CreateProperty<ConversationData>(nameof(ConversationData));
			var conversationData = await conversationStateAccessors.GetAsync(stepContext.Context, () => new ConversationData()).ConfigureAwait(false);
			foreach ( string availableDate in nextAvailableDates)
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
				card.Buttons.Add(new CardAction(ActionTypes.ImBack, title: nextAvailableDates[x], value: nextAvailableDates[x]));
			
			// Send the card(s) to the user as an attachment to the activity
			await stepContext.Context.SendActivityAsync(MessageFactory.Attachment(card.ToAttachment()), cancellationToken).ConfigureAwait(false);

			// Aguarda uma resposta
			string retryText = "Por favor, escolha uma destas datas: ";
			foreach (string nextavailabledate in nextAvailableDates)
				retryText += " " + nextavailabledate;
			return await stepContext.PromptAsync("dateprompt", new PromptOptions { Prompt = null, RetryPrompt = MessageFactory.Text(retryText) }, cancellationToken).ConfigureAwait(false);

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
				Text = "Você prefere atendimento no período da manhã (08h as 13h) ou da tarde (13h às 18h)?",
				Buttons = new List<CardAction>
				{
					new CardAction(ActionTypes.ImBack, title: $"manhã", value: "manhã"),
					new CardAction(ActionTypes.ImBack, title: $"tarde", value: "tarde"),
				},
			};

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
			var msg = $"Ok! Obrigado. Sua visita técnica {_dialogDictionary.Emoji.ManMechanic} está agendada para o dia {stepContext.Values["data"]} no período da {stepContext.Values["turno"]}.\n48 horas antes do agendamento disponibilizaremos informações do técnico que fará a visita." + _dialogDictionary.Emoji.ThumbsUp;
			await stepContext.Context.SendActivityAsync(MessageFactory.Text(msg), cancellationToken).ConfigureAwait(false);

			// Salva os dados do Customer no banco de dados
			await UpdateCustomer(stepContext).ConfigureAwait(false);

			// Envia os dados do cliente para o Ploomes
			string note = $"dia {(string)stepContext.Values["data"]} turno da {turno}";
			await _ploomesclient.PostContact((string)stepContext.Values["name"], (string)stepContext.Values["phone"], Int32.Parse(Utility.ClearStringNumber((string)stepContext.Values["cep"])), note).ConfigureAwait(false);

			// Termina este diálogo
			return await stepContext.EndDialogAsync().ConfigureAwait(false);

		}
		// Validação: Sim ou Nâo
		private async Task<bool> YesNoValidatorAsync(PromptValidatorContext<string> promptContext, CancellationToken cancellationToken)
		{
			bool IsValid;

			// Verifica se o que o cliente digitou sim ou não
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
		// Valida as datas
		private async Task<bool> DateValidatorAsync(PromptValidatorContext<string> promptContext, CancellationToken cancellationToken)
		{

			// Ponteiro para os dados persistentes da conversa
			var conversationStateAccessors = _conversationState.CreateProperty<ConversationData>(nameof(ConversationData));
			var conversationData = await conversationStateAccessors.GetAsync(promptContext.Context, () => new ConversationData()).ConfigureAwait(false);

			// Busca o que foi digitado
			string choice = (string)promptContext.Context.Activity.Text;

			// Busca novamente as datas disponíveis
			List<string> nextAvailableDates = conversationData.NextAvailableDates;

			// Devolve true or false se a escolha esta dentro da lista de datas disponíveis
			return nextAvailableDates.Contains(choice);
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
		// Atualiza o registro do usuario
		private async Task UpdateCustomer(WaterfallStepContext stepContext)
		{
			// Procura pelo registro do usuario
			Customer customer = _botDbContext.Customers
								.Where(s => s.Id == stepContext.Context.Activity.From.Id)
								.FirstOrDefault();

			// Confirma que achou o registro
			if (customer != null)
			{
				// Salva o nome do cliente
				stepContext.Values["name"] = customer.Name;
				stepContext.Values["phone"] = customer.MobilePhone;

				// Atualiza o cliente
				if (!string.IsNullOrEmpty((string)stepContext.Values["cep"]))
					customer.Zip = (string)stepContext.Values["cep"];
				if (!string.IsNullOrEmpty((string)stepContext.Values["data"]))
					customer.Tag1 = (string)stepContext.Values["data"];
				if (!string.IsNullOrEmpty((string)stepContext.Values["turno"]))
					customer.Tag2 = (string)stepContext.Values["turno"];
				if (!string.IsNullOrEmpty((string)stepContext.Values["cidade"]))
					customer.City = (string)stepContext.Values["cidade"];
				if (!string.IsNullOrEmpty((string)stepContext.Values["estado"]))
					customer.State = (string)stepContext.Values["estado"];
				if (!string.IsNullOrEmpty((string)stepContext.Values["bairro"]))
					customer.Neighborhood = (string)stepContext.Values["bairro"];

				// Salva o cliente no banco
				_botDbContext.Customers.Update(customer);
				await _botDbContext.SaveChangesAsync().ConfigureAwait(false);

				// Ponteiro para os dados persistentes da conversa
				var conversationStateAccessors = _conversationState.CreateProperty<ConversationData>(nameof(ConversationData));
				var conversationData = await conversationStateAccessors.GetAsync(stepContext.Context, () => new ConversationData()).ConfigureAwait(false);

				// Salva os dados do usuário no objeto persistente da conversa - sem os External Accounts - Dicionar não comporta recursão dos filhos
				conversationData.Customer = customer.ShallowCopy();
			}
		}
		// Busca detalhes do endereço com base no CEP
		private static void GetAddressFromZip(WaterfallStepContext stepContext, string cep)
		{

			stepContext.Values["bairro"] = string.Empty;
			stepContext.Values["cidade"] = string.Empty;
			stepContext.Values["estado"] = string.Empty;

			// Consulta o cep nos Correios para buscar cidade, estado e bairro
			var _correios = new Correios.AtendeClienteClient();
			var _cp = new Correios.consultaCEP
			{
				cep = cep.Replace("-", "")
			};

			try
			{
				var _return = _correios.consultaCEP(_cp);

				if (_return.@return != null)
				{
					stepContext.Values["bairro"] = _return.@return.bairro;
					stepContext.Values["cidade"] = _return.@return.cidade;
					stepContext.Values["estado"] = _return.@return.uf;
				}

			}
			catch (Exception)
			{
			}

			_correios.Close();

			return;
		}
	}
}
