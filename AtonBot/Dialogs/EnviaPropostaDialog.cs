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

namespace MrBot.Dialogs
{
	public class EnviaPropostaDialog: ComponentDialog
	{
		// Dicionario de frases e ícones 
		private readonly DialogDictionary _dialogDictionary;
		private readonly BotDbContext _botDbContext;
		private readonly ConversationState _conversationState;
		private readonly PloomesClient _ploomesclient;
		private readonly Customer _customer;
		private readonly Deal _deal;

		public EnviaPropostaDialog(BotDbContext botContext, DialogDictionary dialogDictionary, ConversationState conversationState, IBotTelemetryClient telemetryClient, PloomesClient ploomesClient, QuerAtendimentoDialog querAtendimentoDialog, Customer customer, Deal deal)
			: base(nameof(EnviaPropostaDialog))
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

			// Adiciona um diálogo de prompt de texto para continuar
			AddDialog(new TextPrompt("sim_nao", YesNoValidatorAsync));
			// Adiciona um diálogo de texto sem validação
			AddDialog(new TextPrompt("TextPrompt"));


			// Adiciona um dialogo WaterFall com os passos
			AddDialog(new WaterfallDialog(nameof(WaterfallDialog), new WaterfallStep[]
			{
				ApresentaPropostaStepAsync,
				AceitaPropostaStepAsync
			}));

			AddDialog(querAtendimentoDialog);

			// Configura para iniciar no WaterFall Dialog
			InitialDialogId = nameof(WaterfallDialog);
		}

		// Apresenta a Proposta
		private async Task<DialogTurnResult> ApresentaPropostaStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{

			// Ponteiro para os dados persistentes da conversa
			var conversationStateAccessors = _conversationState.CreateProperty<ConversationData>(nameof(ConversationData));
			var conversationData = await conversationStateAccessors.GetAsync(stepContext.Context, () => new ConversationData()).ConfigureAwait(false);

			// Se já apresentou a proposta antes
			if ( conversationData.PropostaEnviada)
            {
				// Avisa que a proposta está pronta, mas deu erro e não conseguiu obter no sistema.
				await stepContext.Context.SendActivityAsync(MessageFactory.Text($"Você conseguiu avalia a proposta?"), cancellationToken).ConfigureAwait(false);

				// Pergunta se podemos prosseguir com a proposta? Sim / Não
				var card = new HeroCard
				{
					Text = $"Podemos prosseguir?",
					Buttons = new List<CardAction>
					{
						new CardAction(ActionTypes.ImBack, title: "Sim", value: "sim"),
						new CardAction(ActionTypes.ImBack, title: "Não", value: "não"),
					},
				};
				// Send the card(s) to the user as an attachment to the activity
				await stepContext.Context.SendActivityAsync(MessageFactory.Attachment(card.ToAttachment()), cancellationToken).ConfigureAwait(false);

				// Muda o estágio do Lead para Proposta Apresentada
				_deal.StageId = AtonStageId.PropostaApresentada;

				// Patch Deal
				await _ploomesclient.PatchDeal(_deal).ConfigureAwait(false);

				// Aguarda uma resposta
				return await stepContext.PromptAsync("sim_nao", new PromptOptions { Prompt = null, RetryPrompt = MessageFactory.Text("Por favor, digite: Sim ou Não") }, cancellationToken).ConfigureAwait(false);
			}
			else
            {
				// Busca a Quote
				Quote quote = await _ploomesclient.GetQuote(_deal.Id).ConfigureAwait(false);

				// Se localizou
				if (quote != null && !string.IsNullOrEmpty(quote.DocumentUrl))
				{
					// Envia o PDF com a proposta
					await EnviaPDF(stepContext, "Proposta_Comercial", "Sua proposta comercial está pronta. Aqui está o PDF com a mesma:", quote.DocumentUrl, cancellationToken).ConfigureAwait(false);

					// Pergunta se podemos prosseguir com a proposta? Sim / Não
					var card = new HeroCard
					{
						Text = $"Podemos prosseguir com a proposta?",
						Buttons = new List<CardAction>
					{
						new CardAction(ActionTypes.ImBack, title: "Sim", value: "sim"),
						new CardAction(ActionTypes.ImBack, title: "Não", value: "não"),
					},
					};
					// Send the card(s) to the user as an attachment to the activity
					await stepContext.Context.SendActivityAsync(MessageFactory.Attachment(card.ToAttachment()), cancellationToken).ConfigureAwait(false);

					// Muda o estágio do Lead para Proposta Apresentada
					_deal.StageId = AtonStageId.PropostaApresentada;

					// Patch Deal
					await _ploomesclient.PatchDeal(_deal).ConfigureAwait(false);

					// Marca que apresentou a proposta
					conversationData.PropostaEnviada = true;

					// Aguarda uma resposta
					return await stepContext.PromptAsync("sim_nao", new PromptOptions { Prompt = null, RetryPrompt = MessageFactory.Text("Por favor, digite: Sim ou Não") }, cancellationToken).ConfigureAwait(false);
				}
				else
				{
					// Avisa que a proposta está pronta, mas deu erro e não conseguiu obter no sistema.
					await stepContext.Context.SendActivityAsync(MessageFactory.Text($"Sua proposta está pronta e validada. Contudo, eu não estou conseguindo buscar sua proposta no sistema. {_dialogDictionary.Emoji.DisapointedFace}"), cancellationToken).ConfigureAwait(false);

					// Finaliza o diálogo
					return await stepContext.EndDialogAsync().ConfigureAwait(false);
				}

			}
		}

		// Aceita Proposta
		private async Task<DialogTurnResult> AceitaPropostaStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{
			// Busca a opção informada no passo anterior
			string choice = ((string)stepContext.Result).ToLower();
			if (choice == "s" | choice == "sim")
            {
				// Pede para enviar comprovante por email
				await stepContext.Context.SendActivityAsync(MessageFactory.Text($"Ótimo!\n Favor enviar o comprovante de pagamento da primeira parcela por email para comprovante@atonservices.com.br. Os dados para pagamentos estão indicados na proposta."), cancellationToken).ConfigureAwait(false);

				// Marca o campo Proposta Aceita
				_deal.MarcaPropostaAceita(true);

				// Patch Deal
				await _ploomesclient.PatchDeal(_deal).ConfigureAwait(false);

				// Muda o estágio do Lead para Proposta Aceita
				_deal.StageId = AtonStageId.PropostaAceita;

				// Patch Deal --- tem que fazer novamente, porque não permite mudar o estágio sem antes marcar o aceite da proposta.
				await _ploomesclient.PatchDeal(_deal).ConfigureAwait(false);
			}
			else
            {
				// Pede para resolver dúvidas por email
				await stepContext.Context.SendActivityAsync(MessageFactory.Text($"Em caso de alguma dúvida entre em contato pelo email atonservices@atonservices.com.br"), cancellationToken).ConfigureAwait(false);

				// Marca o campo Proposta Aceita com False
				_deal.MarcaPropostaAceita(false);

				// Patch Deal
				await _ploomesclient.PatchDeal(_deal).ConfigureAwait(false);
			}

			// Finaliza o diálogo
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
		// Envia um PDF
		private static async Task EnviaPDF(WaterfallStepContext stepContext, string filename, string messagetext, string pdfurl, CancellationToken cancellationToken)
		{
			// Cria um anexo com o PDF
			Attachment attachment = new Attachment
			{
				Name = filename,
				ContentType = "application/pdf",
				ContentUrl = pdfurl,
			};
			IMessageActivity reply = MessageFactory.Text(messagetext);
			reply.Attachments = new List<Attachment>() { attachment };

			// Envia o anexo para o cliente
			await stepContext.Context.SendActivityAsync(reply, cancellationToken).ConfigureAwait(false);

		}


	}
}