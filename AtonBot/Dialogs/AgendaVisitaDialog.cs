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
using ContactCenter.Data;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using PloomesApi;
using ContactCenter.Infrastructure.Clients.GsWhatsApp;
using ContactCenter.Core.Models;

namespace MrBot.Dialogs
{
	public class AgendaVisitaDialog: ComponentDialog
	{
		// Dicionario de frases e ícones 
		private readonly DialogDictionary _dialogDictionary;
		private readonly ApplicationDbContext _botDbContext;
		private readonly ConversationState _conversationState;
		private readonly PloomesClient _ploomesclient;
		private readonly Contact _customer;
		private readonly Deal _deal;
		private readonly GsWhatsAppClient _gsWhatsAppClient;

		public AgendaVisitaDialog(ApplicationDbContext botContext, DialogDictionary dialogDictionary, ConversationState conversationState, IBotTelemetryClient telemetryClient, PloomesClient ploomesClient, QuerAtendimentoDialog querAtendimentoDialog, Contact customer, Deal deal, AskDateDialog askDateDialog, GsWhatsAppClient gsWhatsAppClient)
			: base(nameof(AgendaVisitaDialog))
		{

			// Injected objects
			_dialogDictionary = dialogDictionary;
			_botDbContext = botContext;
			_conversationState = conversationState;
			_ploomesclient = ploomesClient;
			_customer = customer;
			_deal = deal;
			_gsWhatsAppClient = gsWhatsAppClient;

			// Set the telemetry client for this and all child dialogs.
			this.TelemetryClient = telemetryClient;

			// Adiciona um diálogo de prompt de texto para continuar
			AddDialog(new TextPrompt("sim_nao", YesNoValidatorAsync));
			// Adiciona um diálogo de prompt de texto para validar o turno
			AddDialog(new TextPrompt("turnoprompt",TurnoValidatorAsync));
			// Adiciona um diálogo de prompt de texto para validar o horario da manha
			AddDialog(new TextPrompt("HorarioManhaPrompt", HorarioManhaValidatorAsync));
			// Adiciona um diálogo de prompt de texto para validar o horario da tarde
			AddDialog(new TextPrompt("HorarioTardePrompt", HorarioTardeValidatorAsync));
			// Adiciona um diálogo de texto com validaçao da marca do carregador
			AddDialog(new TextPrompt("MarcaCarregadorPrompt", MarcaCarregadorValidatorAsync));
			// Adiciona um diálogo de texto com validaçao de CEP
			AddDialog(new TextPrompt("CepPrompt", CEPValidatorAsync));
			// Adiciona um diálogo de texto sem validação
			AddDialog(new TextPrompt("TextPrompt"));
			// Adiciona um diálogo de texto com validaçao de Email
			AddDialog(new TextPrompt("EmailPrompt", EmailValidatorAsync));
			// Adiciona um diálogo de texto com validaçao de Nome
			AddDialog(new TextPrompt("NamePrompt", NameValidatorAsync));
			// Adiciona um diálogo de texto com validaçao da pergunta Marca do Veículo
			AddDialog(new TextPrompt("MarcaVeiculoPrompt", MarcaVeiculoValidatorAsync));

			// Adiciona um dialogo WaterFall com os 2 passos: Mostra as opções, Executa as opções
			AddDialog(new WaterfallDialog(nameof(WaterfallDialog), new WaterfallStep[]
			{
				AskOpcao1StepAsync,
				AskLastNameStepAsync,
				AskEmailStepAsync,
				AskMarcaVeiculoStepAsync,
				AskMarcaCarregadorStepAsync,
				AskEcondominioStepAsync,
				AskTemAutorizacaoCondominioStepAsync,
				AskCepStepAsync,
				AskAddressNumberAsync,
				AskAddressComplementAsync,
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

		// Verifica se já tem algum agendamento:
		// Se não tem
		//	 Você gostaria de agendar uma visita técnica para realizar a instalação em sua residência?
		// Se já tem:
		//   Quer fazer um novo reagendamento?
		private async Task<DialogTurnResult> AskOpcao1StepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{
			// Texto inicial
			string initialText = "Você gostaria de agendar uma visita técnica para realizar a instalação em sua residência? " + _dialogDictionary.Emoji.Person;

			// Initialize values
			stepContext.Values["phone"] = string.Empty;
			stepContext.Values["ploomesId"] = string.Empty;
			stepContext.Values["ploomesDealId"] = string.Empty;
			stepContext.Values["nomecompleto"] = string.Empty;
			stepContext.Values["cep"] = string.Empty;
			stepContext.Values["data"] = null;
			stepContext.Values["horario"] = string.Empty;
			stepContext.Values["periodo"] = string.Empty;
			stepContext.Values["cidade"] = string.Empty;
			stepContext.Values["uf"] = string.Empty;
			stepContext.Values["bairro"] = string.Empty;
			stepContext.Values["end"] = string.Empty;
			stepContext.Values["numero"] = string.Empty;
			stepContext.Values["complemento"] = string.Empty;
			stepContext.Values["marcaveiculo"] = string.Empty;
			stepContext.Values["marcacarregador"] = string.Empty;

			// Valida que achou o registro
			if (_customer != null)
            {
				stepContext.Values["nomecompleto"] = _customer.FullName ?? string.Empty;
				stepContext.Values["phone"] = _customer.MobilePhone;
				stepContext.Values["ploomesId"] = _customer.Tag1 ?? string.Empty;
				stepContext.Values["ploomesDealId"] = _customer.Tag2 ?? string.Empty;
				stepContext.Values["email"] = _customer.Email;

				// Verifica se tem salvo na base local o ID do cliente salvo no Ploomes
				if (!string.IsNullOrEmpty(_customer.Tag1) && int.TryParse(_customer.Tag1, out int ploomesClientId) )
				{
					// Verifica se já tem um Deal ( Negócio ) salvo para este Cliente
					if ( _deal != null && _deal.Id > 0 && _deal.OtherProperties != null)
					{
						// Busca a data
						DateTime dataAgendamento = (DateTime)_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.DataVisitaTecnica).FirstOrDefault().DateTimeValue;

						// Muda a frase de inicio do diálogo, adequando ao reagendamento
						initialText = $"Olá, {_customer.Name}! Nós agendamos uma visita técnica para o dia {dataAgendamento:dd/MM} 📝.";

						String tecnicoResponsavel;
						String documentoDoTecnico;
						// Se achou a data de agendamento
						if (dataAgendamento != null)
							// Se tem dados do tecnico
							if (_deal.StageId == AtonStageId.VisitaAgendada && _deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.NomeTecnicoVisita).Any())
                            {
								// Adiciona o nome do tecnico a frase inicial
								tecnicoResponsavel = (String)_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.NomeTecnicoVisita).FirstOrDefault().StringValue;
								if (!string.IsNullOrEmpty(tecnicoResponsavel))
								initialText += $"\nO técnico resonsável é {tecnicoResponsavel}";
								// Adiciona o documento do tecnico a frase inicial
								documentoDoTecnico = (String)_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.DocTecnicoVisita).FirstOrDefault().StringValue;
								if (!string.IsNullOrEmpty(documentoDoTecnico))
									initialText += $", documento {documentoDoTecnico}.";
								else
									initialText += ".";
							}

						// Finaliza a frase inicial
						initialText += "\n Você quer reagendar ? ";
					}
				}
			}
            else
            {
				// Não deveria cair aqui
				await stepContext.Context.SendActivityAsync(MessageFactory.Text("Ocorreu algum erro e não achei seu registro. Vamos recomeçar ..."), cancellationToken).ConfigureAwait(false);
				await stepContext.CancelAllDialogsAsync(cancellationToken).ConfigureAwait(false);
            }

			// Create a HeroCard with options for the user to interact with the bot.
			var card = new HeroCard
			{
				Text = initialText,
				Title = "Visita",
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

		// Verifica se digitou sim ou não para a pergunta "quer agendar"
		//		Se sim,
		//          Se não tem Sobrenome, pergunta
		//          Se tem, pula pro proximo passo
		//		Se não, se despede, e encerra
		private async Task<DialogTurnResult> AskLastNameStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{

			// Busca a opção informada no passo anterior
			string choice = ((string)stepContext.Result).ToLower();

			// Se dise que sim
			if (choice == "sim" | choice == "s")
            {
				// se não tem sobrenome
				if (string.IsNullOrEmpty((string)stepContext.Values["nomecompleto"]))
					// pergunta o nome
					return await stepContext.PromptAsync(nameof(TextPrompt), new PromptOptions { Prompt = MessageFactory.Text("Para começar, por favor, me informe seu nome completo:") }, cancellationToken).ConfigureAwait(false);
				else if (!((string)stepContext.Values["nomecompleto"]).Contains(" "))
					// pergunta o sobrenome
					return await stepContext.PromptAsync(nameof(TextPrompt), new PromptOptions { Prompt = MessageFactory.Text("Qual é o seu sobrenome?") }, cancellationToken).ConfigureAwait(false);
				else
					// pula pro proximo passo
					return await stepContext.NextAsync(string.Empty, cancellationToken).ConfigureAwait(false);
			}
			// Se disse que não
			else
			{
				// Finaliza o diálogo atual
				await stepContext.EndDialogAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

				// Chama o diálogo que pergunta se quer atendimento humano
				return await stepContext.BeginDialogAsync(nameof(QuerAtendimentoDialog), null, cancellationToken).ConfigureAwait(false);

			}
		}
		// Pergunta o email
		private async Task<DialogTurnResult> AskEmailStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{
			// Busca a opção informada no passo anterior
			string sobrenome = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(Utility.CleanName((string)stepContext.Result)?.Trim().ToLower());

			// Se informou sobrenome
			if (!string.IsNullOrEmpty(sobrenome))
				// Se o sobrenome contem o nome ( digitou tudo de novo )
				if (sobrenome.Contains((string)stepContext.Values["nomecompleto"]))
					// Salva o que digitou no nome completo
					stepContext.Values["nomecompleto"] = sobrenome;
				else
					// Soma o sobrenome ao nome
					stepContext.Values["nomecompleto"] = stepContext.Values["nomecompleto"] + " " + sobrenome;

			// Atualiza os dados do cliente no banco
			await UpdateCustomer(stepContext).ConfigureAwait(false);

			if ( string.IsNullOrEmpty((string)stepContext.Values["email"]))
				// Pergunta o Email
				return await stepContext.PromptAsync("EmailPrompt", new PromptOptions { Prompt = MessageFactory.Text($"Ótimo. Poderia nos informar o seu email? 📧"), RetryPrompt = MessageFactory.Text("Acho que não está correto .... por favor, me informe seu email:") }, cancellationToken).ConfigureAwait(false);
			else
				// pula pro proximo passo
				return await stepContext.NextAsync(string.Empty, cancellationToken).ConfigureAwait(false);

		}
		// Marca do veículo
		private async Task<DialogTurnResult> AskMarcaVeiculoStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{
			// Busca a opção informada no passo anterior
			string email = ((string)stepContext.Result).ToLower();

			// Se informou o email no passo anterior
			if (!string.IsNullOrEmpty(email))
            {
				// Salva em variável persistente
				stepContext.Values["email"] = email;
				// Atualiza os dados do cliente no banco
				await UpdateCustomer(stepContext).ConfigureAwait(false);
			}

			// Create a HeroCard with options for the user to interact with the bot.
			var card = new HeroCard
			{
				Text = "Qual a marca do seu veículo? 🚗",
				Buttons = new List<CardAction>
				{
					new CardAction(ActionTypes.ImBack, title: "1- Volvo", value: "1"),
					new CardAction(ActionTypes.ImBack, title: "2- BMW", value: "2"),
					new CardAction(ActionTypes.ImBack, title: "3- Audi", value: "3"),
					new CardAction(ActionTypes.ImBack, title: "4- Porsche", value: "4"),
					new CardAction(ActionTypes.ImBack, title: "5- Mercedes-Benz", value: "5"),
					new CardAction(ActionTypes.ImBack, title: "6- Outros", value: "6")
				},
			};

			// Send the card(s) to the user as an attachment to the activity
			await stepContext.Context.SendActivityAsync(MessageFactory.Attachment(card.ToAttachment()), cancellationToken).ConfigureAwait(false);

			// Aguarda uma resposta
			return await stepContext.PromptAsync("MarcaVeiculoPrompt", new PromptOptions { Prompt = null, RetryPrompt = MessageFactory.Text("Desculpe, não entendi. Informe a marca do seu veículo: 1-Volvo; 2-BMW; 3-Audi; 4-Porsche; 5-Mercedes; 6-Outros") }, cancellationToken).ConfigureAwait(false);

		}
		// Tipo de carregador que quer instalar
		private async Task<DialogTurnResult> AskMarcaCarregadorStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{

			// Busca a opção informada no passo anterior
			string typed = ((string)stepContext.Result).ToLower();

			// Salva a opção digitada
			stepContext.Values["marcaveiculo"] = typed;

			// Create a HeroCard with options for the user to interact with the bot.
			var card = new HeroCard
			{
				Text = "Pretende instalar ⚡:",
				Buttons = new List<CardAction>
				{
					new CardAction(ActionTypes.ImBack, title: "1- Volvo Wallbox", value: "1"),
					new CardAction(ActionTypes.ImBack, title: "2- BMW i Wallbox", value: "2"),
					new CardAction(ActionTypes.ImBack, title: "3- Porsche Mobile Charger Connect", value: "3"),
					new CardAction(ActionTypes.ImBack, title: "4- Tomada tipo industrial", value: "4"),
					new CardAction(ActionTypes.ImBack, title: "5- Outros", value: "5"),
				},
			};

			// Send the card(s) to the user as an attachment to the activity
			await stepContext.Context.SendActivityAsync(MessageFactory.Attachment(card.ToAttachment()), cancellationToken).ConfigureAwait(false);

			// Aguarda uma resposta
			return await stepContext.PromptAsync("MarcaCarregadorPrompt", new PromptOptions { Prompt = null, RetryPrompt = MessageFactory.Text("Por favor, digite um número de 1 a 5 conforme as opções: 1- Volvo Wallbox; 2- BMW i Wallbox; 3- Porsche Mobile Charger Connect; 4- Tomada tipo industrial; 5- Outros.") }, cancellationToken).ConfigureAwait(false);

		}
		// Pergunta Se o local é condominio
		private async Task<DialogTurnResult> AskEcondominioStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{
			// Busca a opção informada no passo anterior
			string choice = (string)stepContext.Result;

			// Passo anterior perguntou a marca
			stepContext.Values["marcacarregador"] = choice;

			// Pergunta se o local é um condominio
			var card = new HeroCard
			{
				Title = "Local",
				Text = "O local é um condomínio? 🏘",
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
		// Se o local é condomínio, pergunta Se já obteve autorização
		private async Task<DialogTurnResult> AskTemAutorizacaoCondominioStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{
			// Busca a opção informada no passo anterior
			string choice = ((string)stepContext.Result).ToLower();
			if (choice == "s")
				choice = "sim";
			else if (choice == "n")
				choice = "não";

			// Salva em variável persistente o que foi informado no passo anterior
			stepContext.Values["ehcondominio"] = choice;

			// Se repondeu sim a pergunta É condominio ...
			if (choice == "sim")
				// pergunta se já obteve autorizaçao
				{
					var card = new HeroCard
					{
						Title = "Autorização",
						Text = "Você já tem a autorização do condomínio? 📄",
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
				// pula pro proximo passo
				return await stepContext.NextAsync(string.Empty, cancellationToken).ConfigureAwait(false);

		}
		// Se o local é condominio, e não tem autorização, explica e encerra
		// Se o local não é condominio, ou se é condomíno e já tem autorização ...
		//    Pergunta o CEP
		private async Task<DialogTurnResult> AskCepStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{
			// Busca a opção informada no passo anterior
			string choice = ((string)stepContext.Result).ToLower();
			if (choice == "s")
				choice = "sim";
			else if (choice == "n")
				choice = "não";

			// Salva em variável persistente o que foi informado no passo anterior
			stepContext.Values["temautorizacao"] = choice;

			// Se é condominio, mas não tem autorização
			if ( choice == "não")
            {
				// Explica que tem autorização, e encerra o diálogo
				await stepContext.Context.SendActivityAsync("Você precisará primeiro solicitar a autorização para o condomínio. Por favor, providencie a autorização, e retorne para agendarmos.", cancellationToken: cancellationToken).ConfigureAwait(false);
				return await stepContext.EndDialogAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
			}
			else
				// Pergunta o CEP
				return await stepContext.PromptAsync("CepPrompt", new PromptOptions { Prompt = MessageFactory.Text($"Por gentileza, informe o CEP do local de instalação para checarmos a disponibilidade de nossos técnicos em sua região? {_dialogDictionary.Emoji.OpenMailBox}"), RetryPrompt = MessageFactory.Text("Este não é um Cep válido. Por favor, digite novamente no formato 00000-000") }, cancellationToken).ConfigureAwait(false);

		}
		// Pergunta o numero do endereço
		private async Task<DialogTurnResult> AskAddressNumberAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
			// Busca o cep informado no passo anterior
			string cep = ((string)stepContext.Result).ToLower();

			// Salva o cep em variavel persistente do diálogo
			stepContext.Values["cep"] = cep;

			// Mensagem que vai consultar o CEP
			await stepContext.Context.SendActivityAsync($"Um instante por favor enquanto eu consulto o seu CEP ...", cancellationToken: cancellationToken).ConfigureAwait(false);

			// Chama api dos Correios e salva cidade, bairro, estado
			GetAddressFromZip(stepContext, cep);

			// Se encontrou resultados, mostra,
			if ( ! string.IsNullOrEmpty((string)stepContext.Values["cep"]))
				// Mostra o endereço
				await stepContext.Context.SendActivityAsync($"Certo, {(string)stepContext.Values["end"]}, {(string)stepContext.Values["bairro"]}, {(string)stepContext.Values["cidade"]}", cancellationToken: cancellationToken).ConfigureAwait(false);

			// Pergunta o numero e o complemento
			return await stepContext.PromptAsync("TextPrompt", new PromptOptions { Prompt = MessageFactory.Text($"Me informe por favor o número. 📩") }, cancellationToken).ConfigureAwait(false);

		}
		// Pergunta o complemento do endereço
		private async Task<DialogTurnResult> AskAddressComplementAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{
			// Busca o número informado no passo anterior
			string input = (string)stepContext.Result;

			// Consula se digitou complemento junto
			string numero;
			string line2;
			SplitAddressNumberAndLine2(input, out numero, out line2);

			// Salva o número em variável persitente ao diálogo
			stepContext.Values["numero"] = numero;

			// Se não informou o complemento
			if (string.IsNullOrEmpty(line2))
				// Pergunta o complemento
				return await stepContext.PromptAsync("TextPrompt", new PromptOptions { Prompt = MessageFactory.Text($"Digite o complemento. Caso não tenha digite “não”. 📩") }, cancellationToken).ConfigureAwait(false);
			else
				// Pula pro proximo passo, já passando o complemento digitado
				return await stepContext.NextAsync(line2, cancellationToken).ConfigureAwait(false);
		}

		// Pergunta a Data desejada
		private async Task<DialogTurnResult> AskDateStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{

			// Busca o complemento informado no passo anterior
			string complemento = (string)stepContext.Result;

			// Salva o número em variável persitente ao diálogo
			stepContext.Values["complemento"] = complemento;

			// Chama o diálogo que pergunta a data desejada, dando opções com base no CEP do cliente
			return await stepContext.BeginDialogAsync(nameof(AskDateDialog), (string)stepContext.Values["cep"], cancellationToken).ConfigureAwait(false);
		}

		// Pergunta o turno
		private async Task<DialogTurnResult> AskTurnoStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{
			// Busca a data em formato string que informada no passo anterior
			string choice = ((string)stepContext.Result).PadLeft(2,'0');

			// Ponteiro para os dados persistentes da conversa
			var conversationStateAccessors = _conversationState.CreateProperty<ConversationData>(nameof(ConversationData));
			var conversationData = await conversationStateAccessors.GetAsync(stepContext.Context, () => new ConversationData(), cancellationToken).ConfigureAwait(false);

			// Substitui hifen por barra ( se digitar 15-05 vira 15/07 pra achar a data ), e retira palavra dia, caso tenha sido digitado
			choice = choice.Replace("-", "/").Replace("dia ", "");

			// Busca novamente as datas disponíveis
			List<DateTime> nextAvailableDates = conversationData.NextAvailableDates;

			// Varre as datas pra conferir com que data a string digitada de escolha confere
			stepContext.Values["data"] = string.Empty;
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

			if ( turno == "manhã" || turno == "manha" || turno == "m")
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
			if (turno == "manhã" || turno == "manha" || turno == "m")
				return await stepContext.PromptAsync("HorarioManhaPrompt", new PromptOptions { Prompt = null, RetryPrompt = MessageFactory.Text("Por favor, escolha um destes horários: 8, 9, 10 ou 11 horas.") }, cancellationToken).ConfigureAwait(false);
			else
				return await stepContext.PromptAsync("HorarioTardePrompt", new PromptOptions { Prompt = null, RetryPrompt = MessageFactory.Text("Por favor, escolha um destes horários: 14, 15, 16 ou 17 horas.") }, cancellationToken).ConfigureAwait(false);
		}
		private async Task<DialogTurnResult> QuemAcopmanhaStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{
			// Verifica se não aceitou os horários ofereceidos
			string choice = (string)stepContext.Result;
			if ( choice.ToLower().Contains("não") | choice.ToLower().Contains("outro") | choice.ToLower().Contains("mais opções"))
            {
				string text = "Deseja mais opções de horário? Entre em contato pelo e-mail atonservices@atonservices.com.br ou pelo telefone 11 4673-3810.";
				await stepContext.Context.SendActivityAsync(MessageFactory.Text(text), cancellationToken).ConfigureAwait(false);

				return await stepContext.EndDialogAsync(null, cancellationToken).ConfigureAwait(false);
			}
			else
            {
				// Busca o horario informado no passo anterior
				string horario = Utility.PadronizaHorario((string)stepContext.Result);

				// Salva em variável persitente ao diálogo
				stepContext.Values["horario"] = horario;

				// pergunta o nome da pessoa que vai estar no local
				return await stepContext.PromptAsync("NamePrompt", new PromptOptions { Prompt = MessageFactory.Text("Para finalizar, por favor digite o nome de quem irá acompanhar a visita técnica?"), RetryPrompt = MessageFactory.Text("Desculpe, não entendi. Por favor, digite o nome de quem acompanhará a visita. Ou digite 'cancelar'.") }, cancellationToken).ConfigureAwait(false);
			}

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
				Text = $"As informações do agendamento são essas: 📝\n\nNome: {(string)stepContext.Values["nomecompleto"]}\nCEP: {(string)stepContext.Values["cep"]}\nEndereço: {(string)stepContext.Values["end"]} {(string)stepContext.Values["numero"]} {(string)stepContext.Values["complemento"]}\nData: {dateStr} às {(string)stepContext.Values["horario"]}\nQuem acompanhará a visita técnica: {quemacompanha}\n\nTodas as informações estão corretas?",
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
				await stepContext.Context.SendActivityAsync(MessageFactory.Text("Por favor, aguarde enquanto salvo seu agendamento no nosso sistema... 👨‍💻"), cancellationToken).ConfigureAwait(false);

				string msg;
				// Se o contato não esta cadastrado ainda - nao tem registro do ID
				if (!Int32.TryParse((string)stepContext.Values["ploomesId"], out int ploomesContactId))
					// Insere o cliente no Ploomes
					ploomesContactId = await _ploomesclient.PostContactByParam((string)stepContext.Values["nomecompleto"], (string)stepContext.Values["phone"], (string)stepContext.Values["email"], int.Parse(Utility.ClearStringNumber((string)stepContext.Values["cep"]), CultureInfo.InvariantCulture), (string)stepContext.Values["cidade"], (string)stepContext.Values["uf"], (string)stepContext.Values["bairro"], (string)stepContext.Values["end"], (string)stepContext.Values["numero"], (string)stepContext.Values["complemento"], (string)stepContext.Values["quemacompanha"]).ConfigureAwait(false);

				else
				{
                    // Confere se o Id salvo localmente, existe no Ploomes
                    PloomesApi.PloomesContact contact = await _ploomesclient.GetContact(ploomesContactId).ConfigureAwait(false);
					// Se achou
					if ( contact.Id > 0 )
						// Patch client
						ploomesContactId = await _ploomesclient.PatchContactByParam(ploomesContactId, (string)stepContext.Values["nomecompleto"], (string)stepContext.Values["phone"], (string)stepContext.Values["email"], int.Parse(Utility.ClearStringNumber((string)stepContext.Values["cep"]), CultureInfo.InvariantCulture), (string)stepContext.Values["cidade"], (string)stepContext.Values["uf"], (string)stepContext.Values["bairro"], (string)stepContext.Values["end"], (string)stepContext.Values["numero"], (string)stepContext.Values["complemento"], (string)stepContext.Values["quemacompanha"]).ConfigureAwait(false);
					else
						// Insere o cliente no Ploomes
						ploomesContactId = await _ploomesclient.PostContactByParam((string)stepContext.Values["nomecompleto"], (string)stepContext.Values["phone"], (string)stepContext.Values["email"], int.Parse(Utility.ClearStringNumber((string)stepContext.Values["cep"]), CultureInfo.InvariantCulture), (string)stepContext.Values["cidade"], (string)stepContext.Values["uf"], (string)stepContext.Values["bairro"], (string)stepContext.Values["end"], (string)stepContext.Values["numero"], (string)stepContext.Values["complemento"], (string)stepContext.Values["quemacompanha"]).ConfigureAwait(false);

				}

				// Confirma se conseguiu inserir ou alterar corretamente o Contact no Ploomes
				if ( ploomesContactId > 0  )
                {
					// Salva o Ploomes ID em variável persistente ao diálogo
					stepContext.Values["ploomesId"] = ploomesContactId.ToString(CultureInfo.InvariantCulture);

					// Obtem data, e data com horario de instalacao
					DateTime date = (DateTime)stepContext.Values["data"];
					string strHorario = (string)stepContext.Values["horario"];
					DateTime horario = date.AddHours(Int16.Parse(strHorario.Replace(":00", ""), CultureInfo.InvariantCulture));

					// Obtem a string que diz qual é a opção de instalação
					string opcaodeInstalacao = TraduzMarcaCarregador((string)stepContext.Values["marcacarregador"]);

					// Salva os dados das variáveis do diálogo no objeto Deal injetado e compartilhado
					_deal.Title = (string)stepContext.Values["nomecompleto"];
					_deal.MarcaDataVisitaTecnica((DateTime)stepContext.Values["data"]);
					_deal.MarcaPeriodoVisitaTecnica((string)stepContext.Values["periodo"]);
					_deal.MarcaHorarioVisitaTecnica(horario);
					_deal.MarcaTipoDaInstalacao(opcaodeInstalacao);
					_deal.MarcaEhCondominio((string)stepContext.Values["ehcondominio"] == "sim");
					_deal.ContactId = ploomesContactId;

					// Confere se o Negócio já está salvo no Ploomes
					int ploomesDealId;
                    if ( _deal == null || _deal.Id == 0)
						// Insere o Negocio no Ploomes - Post Deal
						ploomesDealId = await _ploomesclient.PostDeal(_deal).ConfigureAwait(false);

                    else
						// Altera o Negocio no Ploomoes - Patch Deal
						ploomesDealId = await _ploomesclient.PatchDeal(_deal).ConfigureAwait(false);

					stepContext.Values["ploomesDealId"] = ploomesDealId.ToString(CultureInfo.InvariantCulture);

					// Confirma se conseguiu inserir corretamente o Lead
					if (ploomesDealId != 0)
						msg = $"Ok! Obrigado. Sua visita técnica {_dialogDictionary.Emoji.ManMechanic} está agendada para o dia {((DateTime)stepContext.Values["data"]).ToString("dd/MM", CultureInfo.InvariantCulture)} às {(string)stepContext.Values["horario"]}.\nAntes da visita, disponibilizaremos as informações do técnico que irá ao local." + _dialogDictionary.Emoji.ThumbsUp;
					else
						msg = $"Me desculpe, mas ocorreu algum erro e não consegui salvar o seu agendamento. {_dialogDictionary.Emoji.DisapointedFace}";

					// Marca OptIn do usuário
					if (_customer.Id.Contains("-"))
                    {
						string appName = "AtonServices";
						string recipient = _customer.Id.Split("-")[1];
						_ = await _gsWhatsAppClient.OptIn(appName, recipient).ConfigureAwait(false);
					}

				}
				else
					msg = $"Me desculpe, mas ocorreu algum erro e não consegui salvar o seu cadastro. {_dialogDictionary.Emoji.DisapointedFace}";

				// Envia resposta para o cliente
				await stepContext.Context.SendActivityAsync(MessageFactory.Text(msg), cancellationToken).ConfigureAwait(false);

				// Salva os dados do Customer no banco de dados
				await UpdateCustomer(stepContext).ConfigureAwait(false);

			}
			else
				// Envia resposta para o cliente
				await stepContext.Context.SendActivityAsync(MessageFactory.Text("Ok, seu agendamento NÃO foi realizado."), cancellationToken).ConfigureAwait(false);

			// Termina este diálogo
			return await stepContext.EndDialogAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

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
			IsValid = choice == "manhã" || choice == "manha" || choice == "tarde" || choice == "t" || choice == "m";

			// retorna
			return await Task.FromResult(IsValid).ConfigureAwait(false);
		}
		// Validação horário manhã: 8, 9, 10, 11
		private async Task<bool> HorarioManhaValidatorAsync(PromptValidatorContext<string> promptContext, CancellationToken cancellationToken)
		{
			bool IsValid;

			// Verifica se o que o cliente digitou manhã ou tarde
			string choice = promptContext.Context.Activity.Text.ToLower();
			IsValid = choice.Contains("8") | choice.Contains("9")| choice.Contains("10") | choice.Contains("11") | choice.ToLower().Contains("não") | choice.ToLower().Contains("outro") | choice.ToLower().Contains("mais opções");

			// retorna
			return await Task.FromResult(IsValid).ConfigureAwait(false);
		}
		// Validação horário manhã: 14, 15, 16, 17
		private async Task<bool> HorarioTardeValidatorAsync(PromptValidatorContext<string> promptContext, CancellationToken cancellationToken)
		{
			bool IsValid;

			// Verifica se o que o cliente digitou manhã ou tarde
			string choice = promptContext.Context.Activity.Text.ToLower();
			IsValid = choice.Contains("14") | choice.Contains("15") | choice.Contains("16") | choice.Contains("17") | choice.ToLower().Contains("não") | choice.ToLower().Contains("outro") | choice.ToLower().Contains("mais opções");

			// retorna
			return await Task.FromResult(IsValid).ConfigureAwait(false);
		}

		// Tarefa de validação do CEP
		private async Task<bool> CEPValidatorAsync(PromptValidatorContext<string> promptContext, CancellationToken cancellationToken)
		{
			// retorna
			return await Task.FromResult(Utility.IsValidCEP(Utility.FormataCEP(promptContext.Context.Activity.Text))).ConfigureAwait(false);
		}
		// Tarefa de validação do email
		private async Task<bool> EmailValidatorAsync(PromptValidatorContext<string> promptContext, CancellationToken cancellationToken)
		{
			// Verifica se o que o cliente digitou é um email válido
			string typedinfo = promptContext.Context.Activity.Text.ToLower();
			bool IsValid = Utility.IsValidEmail(typedinfo) ;

			// retorna true ou false como Task
			return await Task.FromResult(IsValid).ConfigureAwait(false);
		}
		// Tarefa de validação da Marca do carregador
		private async Task<bool> MarcaCarregadorValidatorAsync(PromptValidatorContext<string> promptContext, CancellationToken cancellationToken)
		{
			string typedBrand = promptContext.Context.Activity.Text.Trim().ToUpperInvariant();
			string[] validBrands = new string[] { "1", "2", "3", "4", "5", "Volvo", "BMW", "Porsche", "tomada", "outros" };

			foreach (string row in validBrands)
			{
				if (row.ToUpperInvariant().Contains(typedBrand.ToUpperInvariant()))
				{
					return await Task.FromResult(true).ConfigureAwait(false);
				}
			}
			return await Task.FromResult(false).ConfigureAwait(false);
		}
		private static string TraduzMarcaCarregador ( string input)
        {
			string[] validBrands = new string[] { "1", "2", "3", "4", "5", "Volvo", "BMW", "Porsche", "tomada", "outros" };
			string[] normalizedBrands = new string[] { "Volvo Wallbox", "BMW i Wallbox", "Porsche Mobile Charger Connect", "Tomada tipo industrial", "Outros"};

			for (int x= 0; x<=5; x++)
			{
				if (validBrands[x].ToUpperInvariant().Contains(input.ToUpperInvariant()))
				{
					return normalizedBrands[x];
				}
			}

			return string.Empty;
		}
		// Valida um nome
		private async Task<bool> NameValidatorAsync(PromptValidatorContext<string> promptContext, CancellationToken cancellationToken)
		{
			string typed = promptContext.Context.Activity.Text.Trim();

			return await Task.FromResult(Utility.IsValidName(typed)).ConfigureAwait(false);
		}
		// Valida a pergunta "Pretende aquirir ...."
		private async Task<bool> MarcaVeiculoValidatorAsync(PromptValidatorContext<string> promptContext, CancellationToken cancellationToken)
		{
			string typed = promptContext.Context.Activity.Text.Trim();

			return await Task.FromResult(typed.Contains("1") | typed.Contains("2") | typed.Contains("3") | typed.Contains("4") | typed.Contains("5") | typed.Contains("6") | typed.ToLower().Contains("volvo") | typed.ToLower().Contains("bmw") | typed.ToLower().Contains("audi") | typed.ToLower().Contains("porche") | typed.ToLower().Contains("mercede") | typed.Contains("outro")).ConfigureAwait(false);
		}

		// Atualiza o registro do usuario
		private async Task UpdateCustomer(WaterfallStepContext stepContext)
		{
            // Procura pelo registro do usuario
            Contact customer = _botDbContext.Contacts
								.Where(s => s.Id == stepContext.Context.Activity.From.Id)
								.FirstOrDefault();

			// Confirma que achou o registro
			if (customer != null)
			{
				// Atualiza o cliente
				if (!string.IsNullOrEmpty((string)stepContext.Values["nomecompleto"]))
                {
					customer.Name = Utility.FirstName((string)stepContext.Values["nomecompleto"]);
					customer.FullName = (string)stepContext.Values["nomecompleto"];
				}
				if (stepContext.Values["ploomesId"] != null)
					customer.Tag1 = (string)stepContext.Values["ploomesId"];
				if (!string.IsNullOrEmpty((string)stepContext.Values["ploomesDealId"]))
					customer.Tag2 = (string)stepContext.Values["ploomesDealId"];
				if (!string.IsNullOrEmpty((string)stepContext.Values["email"]))
					customer.Email = (string)stepContext.Values["email"];
				if (stepContext.Values["data"] != null)
					customer.Tag3 = ((DateTime)stepContext.Values["data"]).ToString("dd/MM",CultureInfo.InvariantCulture);
				if (stepContext.Values["data"] != null && !string.IsNullOrEmpty((string)stepContext.Values["horario"]))
					customer.Tag3 += " às " + (string)stepContext.Values["horario"];

				// Salva o cliente no banco
				_botDbContext.Contacts.Update(customer);
				await _botDbContext.SaveChangesAsync().ConfigureAwait(false);

			}
		}
		// Busca detalhes do endereço com base no CEP
		private static void GetAddressFromZip(WaterfallStepContext stepContext, string cep)
		{

			stepContext.Values["bairro"] = string.Empty;
			stepContext.Values["cidade"] = string.Empty;
			stepContext.Values["uf"] = string.Empty;
			stepContext.Values["end"] = string.Empty;

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
					stepContext.Values["uf"] = _return.@return.uf;
					stepContext.Values["end"] = _return.@return.end;
				}

			}
			catch (Exception)
			{
			}

			_correios.Close();

			return;
		}
		// Quebra o numero e complemento
		private static void SplitAddressNumberAndLine2( string numberAndLine2, out string number, out string line2)
        {
			number = numberAndLine2;
			line2 = string.Empty;
			if (numberAndLine2.Contains("/"))
            {
				string[] splitNumber = numberAndLine2.Split("/");
				number = splitNumber[0].Trim() ;
				line2 = string.Join("/", splitNumber, 1, splitNumber.Length - 1);
			}
			else if (numberAndLine2.Contains("ap"))
			{
				string[] splitNumber = numberAndLine2.Split("ap");
				number = splitNumber[0].Trim();
				line2 = "ap" + string.Join(" ", splitNumber, 1, splitNumber.Length - 1);
			}
			else if (numberAndLine2.Contains(","))
			{
				string[] splitNumber = numberAndLine2.Split(",");
				number = splitNumber[0].Trim();
				line2 = string.Join(" ", splitNumber, 1, splitNumber.Length - 1);
			}
			else if (numberAndLine2.Contains("apto"))
			{
				string[] splitNumber = numberAndLine2.Split("apto");
				number = splitNumber[0].Trim();
				line2 = "apto " + string.Join(" ", splitNumber, 1, splitNumber.Length - 1);
			}
			return;
        }
	}
}