// AtonBot
// Re AgendamentoDialog
//
// Quer re agendar?
// Consulta opções de datas, oferece opçoes, pergunta data
// Pergunta o turno
// Pergunta hora
// Confirma
// Patch o Lead

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

namespace MrBot.Dialogs
{
	public class ReAgendaVisitaDialog: ComponentDialog
	{
		// Dicionario de frases e ícones 
		private readonly DialogDictionary _dialogDictionary;
		private readonly BotDbContext _botDbContext;
		private readonly ConversationState _conversationState;
		private readonly PloomesClient _ploomesclient;
		private readonly Customer _customer;
		private readonly Contact _contact;
		private readonly Deal _deal;

		public ReAgendaVisitaDialog(BotDbContext botContext, DialogDictionary dialogDictionary, ConversationState conversationState, IBotTelemetryClient telemetryClient, PloomesClient ploomesClient, QuerAtendimentoDialog querAtendimentoDialog, Customer customer, Deal deal, Contact contact, AskDateDialog askDateDialog)
			: base(nameof(ReAgendaVisitaDialog))
		{

			// Injected objects
			_dialogDictionary = dialogDictionary;
			_botDbContext = botContext;
			_conversationState = conversationState;
			_ploomesclient = ploomesClient;
			_customer = customer;
			_contact = contact;
			_deal = deal;

			// Set the telemetry client for this and all child dialogs.
			this.TelemetryClient = telemetryClient;

			// Adiciona um diálogo de prompt de texto para continuar
			AddDialog(new TextPrompt("sim_nao", YesNoValidatorAsync));
			// Adiciona um diálogo de prompt de texto com validação das datas
			// Adiciona um diálogo de prompt de texto para validar o turno
			AddDialog(new TextPrompt("turnoprompt",TurnoValidatorAsync));
			// Adiciona um diálogo de prompt de texto para validar o horario da manha
			AddDialog(new TextPrompt("HorarioManhaPrompt", HorarioManhaValidatorAsync));
			// Adiciona um diálogo de prompt de texto para validar o horario da tarde
			AddDialog(new TextPrompt("HorarioTardePrompt", HorarioTardeValidatorAsync));
			// Adiciona um diálogo de texto sem validação
			AddDialog(new TextPrompt("TextPrompt"));

			// Adiciona um dialogo WaterFall com os 2 passos: Mostra as opções, Executa as opções
			AddDialog(new WaterfallDialog(nameof(WaterfallDialog), new WaterfallStep[]
			{
				AskReQuerAgendarStepAsync,
				CheckAnswerStepAsync,
				AskDateStepAsync,
				AskTurnoStepAsync,
				AskHorarioStepAsync,
				QuemAcopmanhaStepAsync,
				ConfirmaDadosStepAsync,
				SaveStepAsync
			}));

			AddDialog(querAtendimentoDialog);
			AddDialog(askDateDialog);

			// Configura para iniciar no WaterFall Dialog
			InitialDialogId = nameof(WaterfallDialog);
		}

		// Verifica se já tem algum agendamento: se não tem
		// 1- Quer agendar?
		//   Gostaria de agendar uma visita técnica para realizar a instalação em sua residência?
		// Se já tem:
		//       Quer fazer um novo reagendamento?
		private async Task<DialogTurnResult> AskReQuerAgendarStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{
			// Ponteiro para os dados persistentes da conversa
			var conversationStateAccessors = _conversationState.CreateProperty<ConversationData>(nameof(ConversationData));
			var conversationData = await conversationStateAccessors.GetAsync(stepContext.Context, () => new ConversationData()).ConfigureAwait(false);

			// Monta frase com informação do tecnico
			string infoTecnicos = string.Empty;

			// Initialize values
			stepContext.Values["ploomesId"] = string.Empty;
			stepContext.Values["ploomesDealId"] = string.Empty;
			stepContext.Values["cep"] = string.Empty;

			// Verifica se tem salvo na base local o ID do cliente salvo no Ploomes
			if (!string.IsNullOrEmpty(_customer.Tag1) && int.TryParse(_customer.Tag1, out int ploomesClientId) )
			{
				// Verifica se já tem um Deal ( Negócio ) salvo para este Cliente
				if ( _deal != null && _deal.Id > 0 && _deal.OtherProperties != null)
				{

					// Busca informação ( nome e documento ) dos tecnicos
					infoTecnicos = GetInfoTecnicosVisita();

					// Se tem informações dos técnicos, e ainda não repassou para o cliente
					if (!string.IsNullOrEmpty(infoTecnicos) && !conversationData.TecnicosVisitaInformado)
					{
						// Informa os dados do(s) técnico(s)
						await stepContext.Context.SendActivityAsync(MessageFactory.Text(infoTecnicos), cancellationToken).ConfigureAwait(false);

						// Marca que já informou
						conversationData.TecnicosVisitaInformado = true;

						// Finaliza
						return await stepContext.EndDialogAsync().ConfigureAwait(false);
					}

					// Busca a data e a hora
					DateTime dataAgendamento = (DateTime)_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.DataVisitaTecnica).FirstOrDefault().DateTimeValue;
					DateTime horaAgendamento = (DateTime)_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.HorarioVisita).FirstOrDefault().DateTimeValue;

					// Envia frase de reagendamento
					await stepContext.Context.SendActivityAsync(MessageFactory.Text($"Nós agendamos uma visita técnica para o dia {dataAgendamento:dd/MM} às {horaAgendamento:HH:mm} 📝."), cancellationToken).ConfigureAwait(false);

					// Create a HeroCard with options for the user to interact with the bot.
					var card = new HeroCard
					{
						Text = "Você quer reagendar ?",
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
                else
                {
					await stepContext.Context.SendActivityAsync(MessageFactory.Text("Ocorreu um erro, nao achei seu cadastro no sistema!"), cancellationToken).ConfigureAwait(false);
					return await stepContext.EndDialogAsync().ConfigureAwait(false);
				}


			}
			else
			{
				await stepContext.Context.SendActivityAsync(MessageFactory.Text("Ocorreu um erro, nao achei seu cadastro no sistema!"), cancellationToken).ConfigureAwait(false);
				return await stepContext.EndDialogAsync().ConfigureAwait(false);
			}

		}

		// Confere se quer reagendar
		private async Task<DialogTurnResult> CheckAnswerStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{
			// Busca a opção informada no passo anterior
			string choice = ((string)stepContext.Result).ToLower();
			if (choice == "s" | choice == "sim")
				return await stepContext.NextAsync(null).ConfigureAwait(false);

			else
            {
				// Finaliza o diálogo atual
				await stepContext.EndDialogAsync().ConfigureAwait(false);

				// Chama o diálogo que pergunta se quer atendimento humano
				return await stepContext.BeginDialogAsync(nameof(QuerAtendimentoDialog), null, cancellationToken).ConfigureAwait(false);
			}

		}
		// Consulta opções de datas com base no CEP, oferece opçoes, pergunta data
		private async Task<DialogTurnResult> AskDateStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{

			// Busca o cep do Contact
			string cep = _contact.ZipCode.ToString();

			// Chama o diálogo que pergunta a data desejada, dando opções com base no CEP do cliente
			return await stepContext.BeginDialogAsync(nameof(AskDateDialog), cep, cancellationToken).ConfigureAwait(false);

		}

		// Pergunta o turno
		private async Task<DialogTurnResult> AskTurnoStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{
			// Busca a data em formato string que informada no passo anterior
			string choice = ((string)stepContext.Result).PadLeft(2,'0');

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
				if ( choice == data.ToString("dd/MM", CultureInfo.InvariantCulture) | choice == data.ToString("dd/MM", CultureInfo.InvariantCulture).Split("/")[0])
                {
					// Salva a data em varivel persitente ao diálogo
					stepContext.Values["data"] = data;
				}
			}

			// Pergunta o Turno desejado
			var card = new HeroCard
			{
				Title = $"Turno 🌗",
				Text = "Você prefere atendimento no período da manhã (08h as 11h) ou da tarde (14h às 17h)?",
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
		// Pergunta o horário
		private async Task<DialogTurnResult> AskHorarioStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
			// Busca o turno
#pragma warning disable CA1308 // Normalize strings to uppercase
			string turno = ((string)stepContext.Result).ToLowerInvariant();
#pragma warning restore CA1308 // Normalize strings to uppercase

			// Salva o turno em varivel persitente ao diálogo
			stepContext.Values["periodo"] = turno;

			// Pergunta o Horário
			var card = new HeroCard
			{
				Title = $"Horário ⏰",
				Text = "Por favor, escolha o horário:",
			};

			if ( turno == "manhã" || turno == "manha")
				card.Buttons = new List<CardAction>
				{
					new CardAction(ActionTypes.ImBack, title: $"08:00", value: "08:00"),
					new CardAction(ActionTypes.ImBack, title: $"09:00", value: "09:00"),
					new CardAction(ActionTypes.ImBack, title: $"10:00", value: "10:00"),
					new CardAction(ActionTypes.ImBack, title: $"11:00", value: "11:00"),
				};
			else
				card.Buttons = new List<CardAction>
				{
					new CardAction(ActionTypes.ImBack, title: $"14:00", value: "14:00"),
					new CardAction(ActionTypes.ImBack, title: $"15:00", value: "15:00"),
					new CardAction(ActionTypes.ImBack, title: $"16:00", value: "16:00"),
					new CardAction(ActionTypes.ImBack, title: $"17:00", value: "17:00"),
				};


			// Send the card(s) to the user as an attachment to the activity
			await stepContext.Context.SendActivityAsync(MessageFactory.Attachment(card.ToAttachment()), cancellationToken).ConfigureAwait(false);

			// Aguarda uma resposta
			if (turno == "manhã" || turno == "manha" )
				return await stepContext.PromptAsync("HorarioManhaPrompt", new PromptOptions { Prompt = null, RetryPrompt = MessageFactory.Text("Por favor, escolha um destes horários: 8, 9, 10 ou 11 horas.") }, cancellationToken).ConfigureAwait(false);
			else
				return await stepContext.PromptAsync("HorarioTardePrompt", new PromptOptions { Prompt = null, RetryPrompt = MessageFactory.Text("Por favor, escolha um destes horários: 14, 15, 16 ou 17 horas.") }, cancellationToken).ConfigureAwait(false);
		}
		private async Task<DialogTurnResult> QuemAcopmanhaStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{
			// Busca o horario informado no passo anterior
			string horario = Utility.PadronizaHorario((string)stepContext.Result);

			// Salva em variável persitente ao diálogo
			stepContext.Values["horario"] = horario;

			// pergunta o nome da pessoa que vai estar no local
			return await stepContext.PromptAsync(nameof(TextPrompt), new PromptOptions { Prompt = MessageFactory.Text("Para finalizar, por favor digite o nome de quem irá acompanhar a visita técnica?") }, cancellationToken).ConfigureAwait(false);

		}
		private async Task<DialogTurnResult> ConfirmaDadosStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{
			// Busca o nome informado no passo anterior
			string quemacompanha = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(Utility.CleanName((string)stepContext.Result)?.Trim().ToLower());

			// Salva em variável persitente ao diálogo
			stepContext.Values["quemacompanha"] = quemacompanha;

			// Mostra mensagem resumindo o agendamento, e pede confirmação
			DateTime date = (DateTime)stepContext.Values["data"];
			string dateStr = date.ToString("dd/MM");
			var card = new HeroCard
			{
				Text = $"As informações do reagendamento são essas: 📝\n\nData: {dateStr} às {(string)stepContext.Values["horario"]}\nQuem acompanhará a visita técnica: {quemacompanha}\n\nTodas as informações estão corretas?",
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
		// Salva os dados no banco, salva o Lead no Ploomes, e da mensagem final confirmando o agendamento
		private async Task<DialogTurnResult> SaveStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{

			// Busca a opção informada no passo anterior
			string choice = ((string)stepContext.Result).ToLower();
			if (choice == "s" | choice == "sim")
			{
				// Avisa o cliente para aguardar enquanto salva os dados
				await stepContext.Context.SendActivityAsync(MessageFactory.Text("Por favor, aguarde enquanto salvo seu agendamento no nosso sistema..."), cancellationToken).ConfigureAwait(false);
				string msg;


				// Obtem data, e data com horario de instalacao
				DateTime date = (DateTime)stepContext.Values["data"];
				string strHorario = (string)stepContext.Values["horario"];
				DateTime horario = date.AddHours(Int16.Parse(strHorario.Replace(":00", ""), CultureInfo.InvariantCulture));

				// Salva os dados das variáveis do diálogo no objeto Deal injetado e compartilhado
				_deal.MarcaDataVisitaTecnica((DateTime)stepContext.Values["data"]);
				_deal.MarcaPeriodoVisitaTecnica((string)stepContext.Values["periodo"]);
				_deal.MarcaHorarioVisitaTecnica(horario);

				// Confere se o Negócio já está salvo no Ploomes
				int ploomesDealId;
                if ( _deal == null || _deal.Id == 0)
					// Insere o Negocio no Ploomes - Post Deal
					ploomesDealId = await _ploomesclient.PostDeal(_deal).ConfigureAwait(false);

                else
					// Altera o Negocio no Ploomoes - Patch Deal
					ploomesDealId = await _ploomesclient.PatchDeal(_deal).ConfigureAwait(false);

				// Se alterou o nome da pessoa que acompanha
				if (_contact.OtherProperties.Where(p => p.FieldKey == ContactPropertyId.QuemAcompanhaVisita) != null && (string)stepContext.Values["quemacompanha"] != _contact.OtherProperties.Where(p => p.FieldKey == ContactPropertyId.QuemAcompanhaVisita).FirstOrDefault().StringValue)
                {
					_contact.MarcaQuemAcompanhaVisita((string)stepContext.Values["quemacompanha"]);
					await _ploomesclient.PatchContact(_contact).ConfigureAwait(false);
                }

				// Confirma se conseguiu inserir corretamente o Lead
				if (ploomesDealId != 0)
					msg = $"Ok! Obrigado. Sua visita técnica {_dialogDictionary.Emoji.ManMechanic} foi reagendada para o dia {((DateTime)stepContext.Values["data"]).ToString("dd/MM", CultureInfo.InvariantCulture)} às {(string)stepContext.Values["horario"]}.\nAntes da visita disponibilizaremos informações do técnico que irá ao local." + _dialogDictionary.Emoji.ThumbsUp;
				else
					msg = $"Me desculpe, mas ocorreu algum erro e não consegui salvar o seu agendamento. {_dialogDictionary.Emoji.DisapointedFace}";

				// Envia resposta para o cliente
				await stepContext.Context.SendActivityAsync(MessageFactory.Text(msg), cancellationToken).ConfigureAwait(false);

			}
			else
				// Envia resposta para o cliente
				await stepContext.Context.SendActivityAsync(MessageFactory.Text("Ok, NÃO foi feito nenhum reagendamento, e mantemos a data original."), cancellationToken).ConfigureAwait(false);

			// Termina este diálogo
			return await stepContext.EndDialogAsync().ConfigureAwait(false);

		}
		// Validação: Sim ou Nâo
		private async Task<bool> YesNoValidatorAsync(PromptValidatorContext<string> promptContext, CancellationToken cancellationToken)
		{
			bool IsValid;

			// Verifica se o que o cliente digitou sim ou não
			string choice = promptContext.Context.Activity.Text.ToLower();
			IsValid = choice == "sim" || choice == "não" || choice == "nao" || choice == "s" || choice == "n" ;

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
		// Validação horário manhã: 8, 9, 10, 11
		private async Task<bool> HorarioManhaValidatorAsync(PromptValidatorContext<string> promptContext, CancellationToken cancellationToken)
		{
			bool IsValid;

			// Verifica se o que o cliente digitou manhã ou tarde
			string choice = promptContext.Context.Activity.Text.ToLower();
			IsValid = choice.Contains("8") | choice.Contains("9")| choice.Contains("10") | choice.Contains("11");

			// retorna
			return await Task.FromResult(IsValid).ConfigureAwait(false);
		}
		// Validação horário manhã: 14, 15, 16, 17
		private async Task<bool> HorarioTardeValidatorAsync(PromptValidatorContext<string> promptContext, CancellationToken cancellationToken)
		{
			bool IsValid;

			// Verifica se o que o cliente digitou manhã ou tarde
			string choice = promptContext.Context.Activity.Text.ToLower();
			IsValid = choice.Contains("14") | choice.Contains("15") | choice.Contains("16") | choice.Contains("17");

			// retorna
			return await Task.FromResult(IsValid).ConfigureAwait(false);
		}
		// Procura nos campos personalizados do Deal, os nomes, documento e placa do tecnico reponsáveis pela visita
		// Confere se tem dado no campo Outros Tecnicos.
		// Devolve string com uma linha formatada com as informações encontradas.
		private string GetInfoTecnicosVisita()
		{
			string infoTecnicos = string.Empty;

			// Se tem dados do tecnico que fará a visita
			if (_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.NomeTecnicoVisita).Any() && !string.IsNullOrEmpty(_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.NomeTecnicoVisita).FirstOrDefault().StringValue))
			{
				infoTecnicos += _deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.NomeTecnicoVisita).FirstOrDefault().StringValue;
				// Se tem documento do tecnico 
				if (_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.DocTecnicoVisita).Any() && !string.IsNullOrEmpty(_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.DocTecnicoVisita).FirstOrDefault().StringValue))
					infoTecnicos += ", documento: " + _deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.DocTecnicoVisita).FirstOrDefault().StringValue + "\n";
				else
					infoTecnicos += "\n";
				// Se tem a placa do tecnico que fará a visita
				if (_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.PlacaTecnicoVisita).Any() && !string.IsNullOrEmpty(_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.PlacaTecnicoVisita).FirstOrDefault().StringValue) && ((String)_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.PlacaTecnicoVisita).FirstOrDefault().StringValue).Length > 1)
					infoTecnicos += ", placa: " + _deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.PlacaTecnicoVisita).FirstOrDefault().StringValue + "\n";
				else
					infoTecnicos += "\n";
			}
			// Se tem dados de outros técnicos
			if (_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.OutrosTecnicosVisita).Any() && !string.IsNullOrEmpty(_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.OutrosTecnicosVisita).FirstOrDefault().BigStringValue))
			{
				infoTecnicos += _deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.OutrosTecnicosVisita).FirstOrDefault().BigStringValue + "\n";
				infoTecnicos = "Estes são os técnicos que farão a sua visita:\n" + infoTecnicos;
			}
			else
				infoTecnicos = "Este é o técnico que fará a sua visita:\n" + infoTecnicos;

			return infoTecnicos;
		}

	}
}