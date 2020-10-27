// MrBot 2020
// Profile Dialog 2
// Confere se tem email, e sobrenome
// Necessário para criar conta

using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MrBot.Data;
using MrBot.Models;
using MrBot.CognitiveModels;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MrBot.Dialogs
{
	public class ProfileDialog2 : ComponentDialog
	{
		private const int maxprompts = 3;
		private readonly BotDbContext _botDbContext;
		private readonly DialogDictionary _dialogDictionary;
		private readonly IConfiguration _configuration;
		private readonly ConversationState _conversationState;
		private readonly MisterBotRecognizer _recognizer;

		public ProfileDialog2(BotDbContext botContext, DialogDictionary dialogDictionary, IConfiguration configuration, IBotTelemetryClient telemetryClient, ConversationState conversationState, MisterBotRecognizer recognizer)
			: base(nameof(ProfileDialog2))
		{
			// Injected Objects
			_botDbContext = botContext;
			_dialogDictionary = dialogDictionary;
			_configuration = configuration;
			_conversationState = conversationState;
			_recognizer = recognizer;

			// Set the telemetry client for this and all child dialogs.
			this.TelemetryClient = telemetryClient;

			// This array defines how the Waterfall will execute.
			var waterfallSteps = new WaterfallStep[]
			{
				AskNameStepAsync,
				AskSobreNomeStepAsync,
				AskPhoneStepAsync,
				SaveProfile2StepAsync
			};

			// Add named dialogs to the DialogSet. These names are saved in the dialog state.
			AddDialog(new WaterfallDialog(nameof(WaterfallDialog), waterfallSteps));
			AddDialog(new TextPrompt("NamePrompt", NamePromptValidatorAsync));
			AddDialog(new TextPrompt("EmailPrompt", EmailPromptValidatorAsync));
			AddDialog(new TextPrompt("PhonePrompt", PhonePromptValidatorAsync));
			AddDialog(new TextPrompt(nameof(TextPrompt)));

			// The initial child Dialog to run.
			InitialDialogId = nameof(WaterfallDialog);
		}

		// Pergunta o nome do cliente
		private async Task<DialogTurnResult> AskNameStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{

			// Verifica se inicializou mensagem
			string initmsg = "Eu preciso completar seu cadastro.";

			// Localiza o registro do usuario na base do Bot
			Contact customer = _botDbContext.Contacts
						.Where(s => s.Id == stepContext.Context.Activity.From.Id)
						.Include(t => t.ExternalAccounts)
						.FirstOrDefault();

			// Inicializa variaveis persistentes do Waterfall
			stepContext.Values["mobilephone"] = string.Empty;
			stepContext.Values["name"] = string.Empty;
			stepContext.Values["sobrenome"] = string.Empty;
			stepContext.Values["email"] = string.Empty;

			// Salva em variavel do waterfall, se tem ou nao sobrenome salvo
			stepContext.Values["askemail"] = string.IsNullOrEmpty(customer.Email);

			// Salva em variavel do waterfall, se tem ou nao telefone salvo
			stepContext.Values["askphone"] = string.IsNullOrEmpty(customer.MobilePhone) || !Utility.IsValidPhone(customer.MobilePhone);

			// Se falta algum dado
			if (string.IsNullOrEmpty(customer.Name) || !customer.FullName.Contains(" ") || string.IsNullOrEmpty(customer.MobilePhone) || !Utility.IsValidPhone(customer.MobilePhone))
				// avisa que tem que preencher o cadastro
				await stepContext.Context.SendActivityAsync(initmsg).ConfigureAwait(false);
			else
				// Encerra o diálogo
				return await stepContext.EndDialogAsync().ConfigureAwait(false);

			// Se tem nome 
			if (!string.IsNullOrEmpty(customer.Name))
				// pula a pergunta do Nome, e ja devolve como resposta o nome que recebeu
				return await stepContext.NextAsync(customer.FullName, cancellationToken).ConfigureAwait(false);

			else
				// pergunta o nome do cliente
				return await stepContext.PromptAsync("NamePrompt", new PromptOptions { Prompt = MessageFactory.Text("Por favor, qual é o seu nome?"), RetryPrompt = MessageFactory.Text("Qual é o seu nome? por gentileza ...") }, cancellationToken).ConfigureAwait(false);
		}
		// Pergunta o sobrenome
		private async Task<DialogTurnResult> AskSobreNomeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{

			// salva o que foi digitado no passo anterior
			stepContext.Values["name"] = (string)stepContext.Result;

			// Salva os dados
			await SaveCustomerData(stepContext).ConfigureAwait(false);

			// se não tem sobrenome
			if (!((string)stepContext.Result).Contains(" "))
				// pergunta o sobrenome
				return await stepContext.PromptAsync(nameof(TextPrompt), new PromptOptions { Prompt = MessageFactory.Text("Qual é o seu sobrenome?") }, cancellationToken).ConfigureAwait(false);
			else
				// pula pro proximo passo
				return await stepContext.NextAsync(string.Empty).ConfigureAwait(false);

		}
		// Pergunta o telefone
		private async Task<DialogTurnResult> AskPhoneStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{
			// salva o sobrenome que foi digitado no passo anterior, em variavel do waterfall
			stepContext.Values["email"] = (string)stepContext.Result;

			// Salva os dados
			await SaveCustomerData(stepContext).ConfigureAwait(false);

			// se não tem telefone salvo no cadastro
			if ((bool)stepContext.Values["askphone"])
				// pergunta o telefone
				return await stepContext.PromptAsync("PhonePrompt", new PromptOptions { Prompt = MessageFactory.Text("Por favor, me informe o seu telefone celular, com o DDD."), RetryPrompt = MessageFactory.Text("Preciso um celular válido pra completar seu cadastro.") }, cancellationToken).ConfigureAwait(false);
			else
				// pula pro proximo passo
				return await stepContext.NextAsync(string.Empty).ConfigureAwait(false);
		}

		private async Task<DialogTurnResult> SaveProfile2StepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{
			// salva o telefone que foi digitado no passo anterior
			stepContext.Values["mobilephone"] = (string)stepContext.Result;

			// Responde para o usuário
			var msg = $"Obrigado {Utility.FirstName((string)stepContext.Values["name"])}{_dialogDictionary.Emoji.ThumbsUp}.";
			await stepContext.Context.SendActivityAsync(MessageFactory.Text(msg), cancellationToken).ConfigureAwait(false);

			// Salva os dados
			await SaveCustomerData(stepContext).ConfigureAwait(false);

			// Encerra o diálogo
			return await stepContext.EndDialogAsync().ConfigureAwait(false);

		}

		// Salva os dados
		private async Task SaveCustomerData(WaterfallStepContext stepContext)
		{
			// Procura pelo registro do usuario
			Contact customer = _botDbContext.Contacts
									.Where(s => s.Id == stepContext.Context.Activity.From.Id)
									.FirstOrDefault();

			// Se informou celular
			if (!string.IsNullOrEmpty((string)stepContext.Values["mobilephone"]))
				customer.MobilePhone = (string)stepContext.Values["mobilephone"];

			// Se informou nome ou sobrenome
			if (!string.IsNullOrEmpty((string)stepContext.Values["name"]) | !string.IsNullOrEmpty((string)stepContext.Values["sobrenome"]))
			{
				// atualiza o nome
				customer.Name = (stepContext.Values["name"] + " " + (string)stepContext.Values["sobrenome"]).Trim();
			}

			// Se informou email
			if (!string.IsNullOrEmpty((string)stepContext.Values["email"]))
				customer.Email = (string)stepContext.Values["email"];

			// Salva o cliente no banco
			_botDbContext.Contacts.Update(customer);
			await _botDbContext.SaveChangesAsync().ConfigureAwait(false);

			return;
		}

		// Tarefa de validação do Nome
		private async Task<bool> NamePromptValidatorAsync(PromptValidatorContext<string> promptContext, CancellationToken cancellationToken)
		{

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
		// Tarefa de validação do Email
		private async Task<bool> EmailPromptValidatorAsync(PromptValidatorContext<string> promptContext, CancellationToken cancellationToken)
		{
			bool IsValid;

			// Consulta a quantidade de vezes que ja tentou fazer esta mesma pergunta
			if (promptContext.AttemptCount < maxprompts)
				// Verifica se é um email valido, ou se disse não
				IsValid = Utility.IsValidEmail(promptContext.Context.Activity.Text) | (promptContext.AttemptCount > 1 & (promptContext.Context.Activity.Text.Contains("não") | promptContext.Context.Activity.Text.Contains("nao")));
			else
				// Se ja atingiu o máximo de tentativas, desiste e devolve True
				IsValid = true;

			// retorna
			return await Task.FromResult(IsValid).ConfigureAwait(false);
		}

		// Tarefa de validacao do Telefone
		private async Task<bool> PhonePromptValidatorAsync(PromptValidatorContext<string> promptContext, CancellationToken cancellationToken)
		{
			string phone = Utility.ClearStringNumber(promptContext.Context.Activity.Text);
			return await Task.FromResult(Utility.IsValidPhone(phone)).ConfigureAwait(false);

		}
	}
}
