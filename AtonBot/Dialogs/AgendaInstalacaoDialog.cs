// AtonBot
// Agendamento2Dialog
//
// Apresenta a proposta
// Confirma se aceita
// Pede o CPF
// Oferece uma das 2 oções de Data e hora informadas pela instaladora
// Pergunta o nome da pessoa que vai acompanhar
// Envia o boleto
// Pede para enviar comprovante do depósito para o email

using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using MrBot.Data;
using MrBot.Models;
using System.Threading;
using System.Threading.Tasks;
using PloomesApi;
using Microsoft.Bot.Schema;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Globalization;

namespace MrBot.Dialogs
{
	public class AgendaInstalacaoDialog : ComponentDialog
	{
		// Dicionario de frases e ícones 
		private readonly DialogDictionary _dialogDictionary;
		private readonly BotDbContext _botDbContext;
		private readonly ConversationState _conversationState;
		private readonly PloomesClient _ploomesclient;
		private readonly Customer _customer;
		private readonly Contact _contact;
		private readonly Deal _deal;

		public AgendaInstalacaoDialog(BotDbContext botContext, DialogDictionary dialogDictionary, ConversationState conversationState, IBotTelemetryClient telemetryClient, PloomesClient ploomesClient, QuerAtendimentoDialog querAtendimentoDialog, Customer customer, Deal deal, Contact contact)
			: base(nameof(AgendaInstalacaoDialog))
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
			// Adiciona um diálogo de texto sem validação
			AddDialog(new TextPrompt("TextPrompt"));
			// Adiciona um diálogo de prompt de texto com validação das datas
			AddDialog(new TextPrompt("dateprompt", DateValidatorAsync));
			// Adiciona um diálogo de prompt de texto para validar o turno
			AddDialog(new TextPrompt("turnoprompt", TurnoValidatorAsync));
			// Adiciona um diálogo de prompt de texto para validar o horario da manha
			AddDialog(new TextPrompt("HorarioManhaPrompt", HorarioManhaValidatorAsync));
			// Adiciona um diálogo de prompt de texto para validar o horario da tarde
			AddDialog(new TextPrompt("HorarioTardePrompt", HorarioTardeValidatorAsync));
			// Adiciona um diálogo com prompot de texto e validação de CPF;
			AddDialog(new TextPrompt("CpfPrompt", CpfValidatorAsync));


			// Adiciona um dialogo WaterFall com os passos
			AddDialog(new WaterfallDialog(nameof(WaterfallDialog), new WaterfallStep[]
			{
				AskQuerReagendarStepAsync,
				CheckHumanHelpStepAsync,
				AskCPFStepAsync,
				AskDateStepAsync,
				AskTurnoStepAsync,
				AskHorarioStepAsync,
				QuemAcopmanhaStepAsync,
				ConfirmaDadosStepAsync,
				SaveStepAsync
			}));

			AddDialog(querAtendimentoDialog);

			// Configura para iniciar no WaterFall Dialog
			InitialDialogId = nameof(WaterfallDialog);
		}

		// Se tem boleto
		//    Envia o boleto para o contato

		// Verifica se já tem algum agendamento:
		// Se já tem:
		//       Quer fazer um novo reagendamento
		//           Sim: prossegue e reagenda
		//           Nao: diálogo "Quer falar com atendente?"
		// Se não tem:
		//       Pula pro proximo passo
		private async Task<DialogTurnResult> AskQuerReagendarStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{
			// Confere se tem boleto
			if (_deal.OtherProperties != null && _deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.BoletoAttachmentId).Any() && _deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.BoletoAttachmentId).FirstOrDefault().IntegerValue != null && (long)_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.BoletoAttachmentId).FirstOrDefault().IntegerValue > 0)
            {
				// pega o Id do Attachmento do Boleto
				long boletoAttId = (long)_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.BoletoAttachmentId).FirstOrDefault().IntegerValue;
				// busca o Attachment do Boleto
				PloomesAttachment attachment = await _ploomesclient.GetAttachment(boletoAttId).ConfigureAwait(false);
				// confere se achou
				if ( attachment != null && ! string.IsNullOrEmpty(attachment.Url))
					// Envia o anexo
					await EnviaAnexo(stepContext, "Boleto", "Aqui está o boleto de pagamento", attachment.Url, attachment.ContentType, cancellationToken).ConfigureAwait(false);
			}

			string infoTecnicos;

			// Verifica já agendou a Instalação
			if ( _deal.OtherProperties != null && _deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.DataInstalacao).Any() && _deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.DataInstalacao).FirstOrDefault().DateTimeValue != null)
			{
				// Busca a data
				DateTime dataAgendamento = (DateTime)_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.DataInstalacao).FirstOrDefault().DateTimeValue;
				DateTime horarioAgendamento = (DateTime)_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.HorarioInstalacao).FirstOrDefault().DateTimeValue;

				// Informa que a instalação está agendada, e confirma a data e hora
				await stepContext.Context.SendActivityAsync(MessageFactory.Text($"Nós agendamos sua instalação para o dia {dataAgendamento:dd/MM} às {horarioAgendamento:HH:mm}. 📝"), cancellationToken).ConfigureAwait(false);

				// Busca informação ( nome e documento ) dos tecnicos
				infoTecnicos = GetInfoTecnicos();

				// Se tem informações dos técnicos
				if (!string.IsNullOrEmpty(infoTecnicos))
                    // Informa os dados do(s) técnico(s)
                    await stepContext.Context.SendActivityAsync(MessageFactory.Text("Estes são os técnicos responsáveis:\n" + infoTecnicos), cancellationToken).ConfigureAwait(false);

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
				// Frase inicial
				await stepContext.Context.SendActivityAsync(MessageFactory.Text($"Vamos agendar a sua instalação?"), cancellationToken).ConfigureAwait(false);

				// Pula pro proximo passo
				return await stepContext.NextAsync(string.Empty).ConfigureAwait(false);
			}

		}
		// Se digitou não no passo anterior
		//    chama o diálogo que pergunta se quer antedimento humano
		// Caso contrario
		//    pula pro proximo passo
		private async Task<DialogTurnResult> CheckHumanHelpStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
			// Busca a opção informada no passo anterior
			string choice = ((string)stepContext.Result).ToLower();

			// Se dise que sim
			if (choice == "sim" | choice == "s")
			{
				// pula pro proximo passo
				return await stepContext.NextAsync(string.Empty).ConfigureAwait(false);
			}
			// Se disse que não
			else
			{
				// Finaliza o diálogo atual
				await stepContext.EndDialogAsync().ConfigureAwait(false);

				// Chama o diálogo que pergunta se quer atendimento humano
				return await stepContext.BeginDialogAsync(nameof(QuerAtendimentoDialog), null, cancellationToken).ConfigureAwait(false);
			}
		}
		// Se ainda não tem CPF
		//     pede o CPF
		// Se ja tem
		//     pula pro proximo passo
		private async Task<DialogTurnResult> AskCPFStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{
			// Confere se tem CPF salvo
			if (_contact.CPF ==null || string.IsNullOrEmpty((string)_contact.CPF))
				// pergunta o CPF do cliente
				return await stepContext.PromptAsync("CpfPrompt", new PromptOptions { Prompt = MessageFactory.Text("Para continuar, preciso que você me informe o seu CPF, por favor."), RetryPrompt = MessageFactory.Text("Este não é um CPF ou CNPJ Válido. Por favor, digite novamente:") }, cancellationToken).ConfigureAwait(false);

			else
				// se já tem CPF ou CNPJ salvo, pula pro proximo passo
				return await stepContext.NextAsync(null).ConfigureAwait(false);
		}
		// Salva o CPF digitado no passo anterior
		// Consulta opções de datas com base no CEP, oferece opçoes, pergunta data
		private async Task<DialogTurnResult> AskDateStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{
			// Busca o que foi informado no passo anterior
			string lastinput = (string)stepContext.Result;
			// Se foi digitado algo ( perguntou o CPF )
			if (!string.IsNullOrEmpty(lastinput))
            {
				// salva o CPF no cadastro do Contact: Campo Register!
				_contact.Register = Utility.ClearStringNumber(lastinput);

				// Altera o contato no Ploomes - pra salvar quem acompanha a instalação
				_ = await _ploomesclient.PatchContact(_contact).ConfigureAwait(false);

				// Diz: Obrigado!
				await stepContext.Context.SendActivityAsync(MessageFactory.Text($"Obrigado!"), cancellationToken).ConfigureAwait(false);
			}

			// Procura as opções de data com base no CEP do Contact
			List<DateTime> nextAvailableDates = GetNextAvailableDates(_contact.ZipCode.ToString(CultureInfo.InvariantCulture));

			// Salva as datas disponiveis em variavel persistente
			stepContext.Values["nextAvailableDates"] = nextAvailableDates;

			// Salva as datas disponíveis no conversationData - para poder se acessado na função de validação
			var conversationStateAccessors = _conversationState.CreateProperty<ConversationData>(nameof(ConversationData));
			var conversationData = await conversationStateAccessors.GetAsync(stepContext.Context, () => new ConversationData()).ConfigureAwait(false);
			foreach (DateTime availableDate in nextAvailableDates)
				conversationData.AddAvailableDate(availableDate);

			// Monta HeroCard para perguntar a data desejada, dentro das opções disponíveis
			var card = new HeroCard
			{
				Title = $"Agendamento {_dialogDictionary.Emoji.Calendar}",
				Text = "Para seu endereço temos as seguintes datas disponíveis:",
				Buttons = new List<CardAction> { }
			};

			// Adiciona botões para as datas disponíveis
			for (int x = 0; x <= nextAvailableDates.Count - 1; x++)
				card.Buttons.Add(new CardAction(ActionTypes.ImBack, title: nextAvailableDates[x].ToString("dd/MM", CultureInfo.InvariantCulture), value: nextAvailableDates[x]));

			// Send the card(s) to the user as an attachment to the activity
			await stepContext.Context.SendActivityAsync(MessageFactory.Attachment(card.ToAttachment()), cancellationToken).ConfigureAwait(false);

			// Aguarda uma resposta
			string retryText = "Por favor, escolha uma destas datas: ";
			foreach (DateTime nextavailabledate in nextAvailableDates)
				retryText += " " + nextavailabledate.ToString("dd/MM", CultureInfo.InvariantCulture);
			return await stepContext.PromptAsync("dateprompt", new PromptOptions { Prompt = null, RetryPrompt = MessageFactory.Text(retryText) }, cancellationToken).ConfigureAwait(false);

		}

		// Pergunta o turno
		private async Task<DialogTurnResult> AskTurnoStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
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

			if (turno == "manhã" || turno == "manha")
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
			if (turno == "manhã" || turno == "manha")
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
				Text = $"As informações para instalação são essas: 📝\n\nData: {dateStr} às {(string)stepContext.Values["horario"]}\nQuem acompanhará a visita técnica: {quemacompanha}\n\nVoce confirma o agemento da instalação?",
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
				DateTime horarioInstalacao = date.AddHours(Int16.Parse(strHorario.Replace(":00", ""), CultureInfo.InvariantCulture));

				// Salva os dados das variáveis do diálogo no objeto Deal injetado e compartilhado
				_deal.MarcaDataInstalacao((DateTime)stepContext.Values["data"]);
				_deal.MarcaHorarioInstalacao(horarioInstalacao);

				// Salva quem acompanha a instlação
				_contact.MarcaQuemAcompanhaInstalacao((string)stepContext.Values["quemacompanha"]);

				// Altera o Negocio no Ploomoes - Patch Deal
				int ploomesDealId = await _ploomesclient.PatchDeal(_deal).ConfigureAwait(false);

				// Confirma se conseguiu inserir corretamente o Lead
				if (ploomesDealId != 0)
					msg = $"Ok! Obrigado. 👌\nSua instalação está agendada para o dia {((DateTime)stepContext.Values["data"]).ToString("dd/MM", CultureInfo.InvariantCulture)} às {(string)stepContext.Values["horario"]}.\nAntes da visita disponibilizaremos informações do técnico que irá ao local. 👨‍🔧" + _dialogDictionary.Emoji.ThumbsUp;
				else
					msg = $"Me desculpe, mas ocorreu algum erro e não consegui salvar o seu agendamento. {_dialogDictionary.Emoji.DisapointedFace}";

				// Envia resposta para o cliente
				await stepContext.Context.SendActivityAsync(MessageFactory.Text(msg), cancellationToken).ConfigureAwait(false);

				// Altera o contato no Ploomes - pra salvar quem acompanha a instalação
				_ = await _ploomesclient.PatchContact(_contact).ConfigureAwait(false);
			}
			else
				// Envia resposta para o cliente
				await stepContext.Context.SendActivityAsync(MessageFactory.Text("Ok, seu agendamento NÃO foi realizado."), cancellationToken).ConfigureAwait(false);

			// Termina este diálogo
			return await stepContext.EndDialogAsync().ConfigureAwait(false);
		}

		// Validação: Sim ou Nâo
		private async Task<bool> YesNoValidatorAsync(PromptValidatorContext<string> promptContext, CancellationToken cancellationToken)
		{
			bool IsValid;

			// Verifica se o que o cliente digitou sim ou não
			string choice = promptContext.Context.Activity.Text.ToLower();
			IsValid = choice == "sim" || choice == "não" || choice == "nao" || choice == "s" || choice == "n";

			// retorna
			return await Task.FromResult(IsValid).ConfigureAwait(false);
		}
		private async Task<bool> DateValidatorAsync(PromptValidatorContext<string> promptContext, CancellationToken cancellationToken)
		{

			// Busca o que foi digitado
			string choice = (string)promptContext.Context.Activity.Text;
			choice = choice.PadLeft(2, '0');

			// Ponteiro para os dados persistentes da conversa
			var conversationStateAccessors = _conversationState.CreateProperty<ConversationData>(nameof(ConversationData));
			var conversationData = await conversationStateAccessors.GetAsync(promptContext.Context, () => new ConversationData()).ConfigureAwait(false);

			// Substitui hifen por barra ( se digitar 15-05 vira 15/07 pra achar a data ), e retira palavra dia, caso tenha sido digitado
			choice = choice.Replace("-", "/").Replace("dia ", "");

			// Busca novamente as datas disponíveis
			List<DateTime> nextAvailableDates = conversationData.NextAvailableDates;
			// Array com as escolhas em format string dd/MM
			List<string> validchoices = new List<string>();

			// Adiciona as datas e os dia das datas as possibilidaes de validação- pra validar se o cliente digitar somente o dia
			foreach (DateTime data in nextAvailableDates)
			{
				validchoices.Add(data.ToString("dd/MM", CultureInfo.InvariantCulture));
				validchoices.Add(data.ToString("dd/MM", CultureInfo.InvariantCulture).Split("/")[0]);
			}

			// Devolve true or false se a escolha esta dentro da lista de datas disponíveis
			return validchoices.Contains(choice);
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
			IsValid = choice.Contains("8") | choice.Contains("9") | choice.Contains("10") | choice.Contains("11");

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
		// Busca as próximas datas disponiveis, com base no CEP informado
		private static List<DateTime> GetNextAvailableDates(string cep)
		{
			List<DateTime> nextAvailableDates = new List<DateTime>();

			//TODO: Lógica para obter as próximas datas disponíveis
			int choicesQuantity = 3;
			int nextDateDelay = 7;
			int icep = int.Parse(Utility.ClearStringNumber(cep));
			if (Utility.CepIsCapital(icep))
			{
				nextDateDelay = 5;
			}
			DateTime nextDate = DateTime.Today.AddDays(nextDateDelay + 1);

			do
			{
				nextDate = Utility.GetNextWorkingDay(nextDate);
				nextAvailableDates.Add(nextDate);

			} while (nextAvailableDates.Count < choicesQuantity);

			return nextAvailableDates;
		}
		// Procura nos campos personalizados do Deal, os nomes e documentos dos tecnicos reponsáveis pela instalação
		// Devolve string com uma linha para cada nome + documento se tiver;
		// Podem ser 3 tecnicos. São 6 campos extras, um para o nome, outro para o documento
		private string GetInfoTecnicos()
        {
			string infoTecnicos = string.Empty;

			// Se tem dados do tecnico1
			if (_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.NomeTecnico1).Any() && !string.IsNullOrEmpty((String)_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.NomeTecnico1).FirstOrDefault().StringValue))
			{
				infoTecnicos += _deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.NomeTecnico1).FirstOrDefault().StringValue;
				// Se tem documento do tecnico 1
				if (_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.DocTecnico1).Any() && !string.IsNullOrEmpty((String)_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.DocTecnico1).FirstOrDefault().StringValue))
					infoTecnicos += ", documento: " + (String)_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.DocTecnico1).FirstOrDefault().StringValue + "\n";
				else
					infoTecnicos += "\n";
			}
			// Se tem dados do tecnico2
			if (_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.NomeTecnico2).Any() && !string.IsNullOrEmpty((String)_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.NomeTecnico2).FirstOrDefault().StringValue))
			{
				infoTecnicos += _deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.NomeTecnico2).FirstOrDefault().StringValue;
				// Se tem documento do tecnico 2
				if (_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.DocTecnico2).Any() && !string.IsNullOrEmpty((String)_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.DocTecnico2).FirstOrDefault().StringValue))
					infoTecnicos += ", documento: " + (String)_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.DocTecnico2).FirstOrDefault().StringValue + "\n";
				else
					infoTecnicos += "\n";
			}
			// Se tem dados do tecnico3
			if (_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.NomeTecnico3).Any() && !string.IsNullOrEmpty((String)_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.NomeTecnico3).FirstOrDefault().StringValue))
			{
				infoTecnicos += (String)_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.NomeTecnico3).FirstOrDefault().StringValue + "\n";
				// Se tem documento do tecnico 3
				if (_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.DocTecnico3).Any() && !string.IsNullOrEmpty((String)_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.DocTecnico3).FirstOrDefault().StringValue))
					infoTecnicos += ", documento: " + (String)_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.DocTecnico3).FirstOrDefault().StringValue;
				else
					infoTecnicos += "\n";
			}

			return infoTecnicos;
        }
		// Tarefa de validação do Cpf
		private async Task<bool> CpfValidatorAsync(PromptValidatorContext<string> promptContext, CancellationToken cancellationToken)
		{
			// pega o que foi digitado
			string input = Utility.ClearStringNumber(promptContext.Context.Activity.Text);

			// retorna
			return await Task.FromResult(Utility.IsCpf(input)).ConfigureAwait(false);
		}
		// Envia um Anexo
		private static async Task EnviaAnexo(WaterfallStepContext stepContext, string filename, string messagetext, string pdfurl, string contentType, CancellationToken cancellationToken)
		{
			// Cria um anexo
			Attachment attachment = new Attachment
			{
				Name = filename,
				ContentType = contentType,
				ContentUrl = pdfurl,
			};
			IMessageActivity reply = MessageFactory.Text(messagetext);
			reply.Attachments = new List<Attachment>() { attachment };

			// Envia o anexo para o cliente
			await stepContext.Context.SendActivityAsync(reply, cancellationToken).ConfigureAwait(false);
		}

	}
}