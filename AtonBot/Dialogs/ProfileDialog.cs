// MrBot 2020
// Profile Dialog

using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using MrBot.CognitiveModels;
using MrBot.Data;
using MrBot.Models;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MrBot.Dialogs
{
	public class ProfileDialog : ComponentDialog
	{
		private const int maxprompts = 2;
		private readonly BotDbContext _botDbContext;
		private readonly DialogDictionary _dialogDictionary;
		private readonly ConversationState _conversationState;
		private readonly MisterBotRecognizer _recognizer;

		public ProfileDialog(BotDbContext botContext, DialogDictionary dialogDictionary, ConversationState conversationState, MisterBotRecognizer recognizer, IBotTelemetryClient telemetryClient)
			: base(nameof(ProfileDialog))
		{
			// Injected Objects
			_botDbContext = botContext;
			_dialogDictionary = dialogDictionary;
			_conversationState = conversationState;
			_recognizer = recognizer;

			// Set the telemetry client for this and all child dialogs.
			this.TelemetryClient = telemetryClient;

			// This array defines how the Waterfall will execute.
			var waterfallSteps = new WaterfallStep[]
			{
				NameStepAsync,
				PhoneStepAsync,
				SaveProfileStepAsync
			};

			// Add named dialogs to the DialogSet. These names are saved in the dialog state.
			AddDialog(new WaterfallDialog(nameof(WaterfallDialog), waterfallSteps));
			AddDialog(new TextPrompt("NamePrompt", NamePromptValidatorAsync));
			AddDialog(new TextPrompt("PhonePrompt", PhonePromptValidatorAsync));
			AddDialog(new TextPrompt("Continuar"));

			// The initial child Dialog to run.
			InitialDialogId = nameof(WaterfallDialog);
		}

		// Pergunta o nome do cliente
		private async Task<DialogTurnResult> NameStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{
			// Se apresenta
			await stepContext.Context.SendActivityAsync(MessageFactory.Text($"Oi. Eu sou um Bot {_dialogDictionary.Emoji.InformationDeskPerson}.\nEstou aqui para fazer o agendamento da instalação do seu carregador Volvo.{_dialogDictionary.Emoji.Automobile}"), cancellationToken).ConfigureAwait(false);

			// Se tem nome vindo do canal
			if (!string.IsNullOrEmpty(stepContext.Context.Activity.From.Name.Trim()) && (stepContext.Context.Activity.ChannelId == "facebook" | stepContext.Context.Activity.ChannelId == "whatsapp"))
			{
				// pula a pergunta do Nome, e ja devolve como resposta o nome que recebeu
				return await stepContext.NextAsync(stepContext.Context.Activity.From.Name, cancellationToken).ConfigureAwait(false);

			}
			else
				// pergunta o nome do cliente
				return await stepContext.PromptAsync("NamePrompt", new PromptOptions { Prompt = MessageFactory.Text("Por favor, qual é o seu nome?"), RetryPrompt = MessageFactory.Text("Qual é o seu nome? por gentileza ...") }, cancellationToken).ConfigureAwait(false);
		}

		// Pergunta o Telefone
		private async Task<DialogTurnResult> PhoneStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{
			// Busca o nome informado no passo anterior
			string name = CultureInfo.CurrentCulture.TextInfo.ToTitleCase((Utility.CleanName((string)stepContext.Result))?.Trim().ToLower());

			// Verifica se digitou um nome válido
			if (Utility.IsValidName(name))
			{
				// Guarda o email, informado no passo anterior
				stepContext.Values["name"] = name;

				// Inicializa valor salvo no waterfall para o telefone;
				stepContext.Values["mobilephone"] = string.Empty;

				// Atualiza os dados do cliente no banco
				await UpdateCustomer(stepContext).ConfigureAwait(false);

			}
			else
				// Deixa o nome em branco
				stepContext.Values["name"] = string.Empty;

			// Confere se a conexão e whatsapp, entao o Id é o telefone 
			if (stepContext.Context.Activity.ChannelId == "whatsapp" | stepContext.Context.Activity.ChannelId == "emulator")
			{
				// Extracts WhatsApp customer number inserted at Activity.From.ID
				string mobilephone;
				if (stepContext.Context.Activity.From.Id.Contains("-"))
					mobilephone = stepContext.Context.Activity.From.Id.Split("-")[1];
				else
					mobilephone = stepContext.Context.Activity.From.Id;

				// pula a pergunta do Celular, e ja devolve como resposta o ID do cliente
				return await stepContext.NextAsync(mobilephone, cancellationToken).ConfigureAwait(false);
			}
			else
			{
				// Responde para o usuário
				var msg = $"É um prazer lhe atender, {Utility.FirstName((string)stepContext.Values["name"])}. " + _dialogDictionary.Emoji.ThumbsUp;
				await stepContext.Context.SendActivityAsync(MessageFactory.Text(msg), cancellationToken).ConfigureAwait(false);

				// Pergunta o celular do cliente
				return await stepContext.PromptAsync("PhonePrompt", new PromptOptions { Prompt = MessageFactory.Text("Você pode me infomar o seu celular, com DDD?"), RetryPrompt = MessageFactory.Text("Por favor, informe seu celular, com o código DDD.") }, cancellationToken).ConfigureAwait(false);
			}
		}

		private async Task<DialogTurnResult> SaveProfileStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{
			// Busca o mobilephone informado no passo anterior
			string mobilephone = (string)stepContext.Result;

			// Verifica se digitou um celular válido
			if (Utility.IsValidPhone(mobilephone) | stepContext.Context.Activity.ChannelId == "emulator")
			{
				// Somente se for WhatsApp - caso contrario, já deu obrigado antes ...
				if (stepContext.Context.Activity.ChannelId == "whatsapp" | stepContext.Context.Activity.ChannelId == "emulator")
				{
					// Responde para o usuário
					var msg = $"Obrigado pelo seu contato, {Utility.FirstName((string)stepContext.Values["name"])}." + _dialogDictionary.Emoji.ThumbsUp;
					await stepContext.Context.SendActivityAsync(MessageFactory.Text(msg), cancellationToken).ConfigureAwait(false);
				}
				else
				{
					// Responde para o usuário
					var msg = $"Obrigado!";
					await stepContext.Context.SendActivityAsync(MessageFactory.Text(msg), cancellationToken).ConfigureAwait(false);

				}

				// Guarda o celular, informado no passo anterior
				stepContext.Values["mobilephone"] = Utility.FormataCelular(mobilephone);

				// Atualiza os dados do cliente
				await UpdateCustomer(stepContext).ConfigureAwait(false);

			}

			// Termina este diálogo
			return await stepContext.EndDialogAsync().ConfigureAwait(false);

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
				// Atualiza o cliente
				if (!string.IsNullOrEmpty((string)stepContext.Values["name"]))
                {
					customer.Name = Utility.FirstName((string)stepContext.Values["name"]);
					customer.FullName = (string)stepContext.Values["name"];
				}
				if (!string.IsNullOrEmpty((string)stepContext.Values["mobilephone"]))
					customer.MobilePhone = Utility.ClearStringNumber((string)stepContext.Values["mobilephone"]);
				customer.LastActivity = Utility.HoraLocal();

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

		// Tarefa de validação do Nome
		private async Task<bool> NamePromptValidatorAsync(PromptValidatorContext<string> promptContext, CancellationToken cancellationToken)
		{
			// Clean Utterance
			promptContext.Context.Activity.Text = Utility.CleanUtterance(promptContext.Context.Activity.Text);

			// Call LUIS and gather intent and any potential details
			var luisResult = await _recognizer.RecognizeAsync<MisterBotLuis>(promptContext.Context, cancellationToken).ConfigureAwait(false);

			bool IsValid;
			// Verifica se o score da intenção ( a melhor pontuada ) tem pelo menos 0.5 de pontos ( menos que isso não esta correto )
			if (luisResult.TopIntent().score > 0.55 && luisResult.TopIntent().intent != MisterBotLuis.Intent.None)
				IsValid = false;

			else
			{
				// Consulta a quantidade de vezes que ja tentou fazer esta mesma pergunta
				if (promptContext.AttemptCount < maxprompts)
					// Verifica se é um nome valido
					IsValid = Utility.IsValidName(Utility.CleanName(promptContext.Context.Activity.Text));
				else
					// Se ja atingiu o máximo de tentativas, desiste e devolve True
					IsValid = true;
			}

			// retorna
			return await Task.FromResult(IsValid).ConfigureAwait(false);
		}
		// Tarefa de validacao do Telefone
		private async Task<bool> PhonePromptValidatorAsync(PromptValidatorContext<string> promptContext, CancellationToken cancellationToken)
		{
			bool IsValid;

			// Consulta a quantidade de vezes que ja tentou fazer esta mesma pergunta
			if (promptContext.AttemptCount < maxprompts)
				// Verifica se é um email valido
				IsValid = Utility.IsValidPhone(promptContext.Context.Activity.Text) | promptContext.Context.Activity.Text.Contains("não") | promptContext.Context.Activity.Text.Contains("nao");
			else
				// Se ja atingiu o máximo de tentativas, desiste e devolve True
				IsValid = true;

			// retorna
			return await Task.FromResult(IsValid).ConfigureAwait(false);

		}

	}
}
