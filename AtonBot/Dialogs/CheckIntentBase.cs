// MrBot 2020
// CheckIntent Dialog
// Lida com interrupções do usário, Intenções Luis, e Qna
// Os demais diálogos herdam este diálogo, que pode então ser acionado a qualquer momento

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.LanguageGeneration;
using Microsoft.Bot.Builder.AI.QnA;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Logging;
using MrBot.CognitiveModels;
using Azure.Storage.Blobs;

namespace MrBot.Dialogs
{
	public class CheckIntentBase : ComponentDialog
	{

		// Conversation state - used by Dialog system
		private readonly ConversationState _conversationState;

		// LUIS Recognizer
		private readonly MisterBotRecognizer _recognizer;

		// QnaMaker service
		private readonly QnAMaker _qnaService;

		// Para salvar arquivos
		private readonly BlobContainerClient _blobContainerClient;

		// Score pra aceitar Intencao
		private double minScoreLuis = 0.5;

		// Langague Generation
		readonly Templates _lgTemplates;

		// Logger
		private readonly ILogger _logger;

		public CheckIntentBase(string childDialogId, ConversationState conversationState, MisterBotRecognizer recognizer, CallHumanDialog callHumanDialog, IBotTelemetryClient telemetryClient, Templates lgTemplates, BlobContainerClient blobContainerClient, ILogger<RootDialog> logger, IQnAMakerConfiguration services, QnAMakerMultiturnDialog qnAMakerMultiturnDialog)
			: base(childDialogId)
		{
			// Cognitive injected objects
			_recognizer = recognizer;
			_conversationState = conversationState;
			_lgTemplates = lgTemplates;
			_blobContainerClient = blobContainerClient;
			_logger = logger;

			// QnaMaker service
			_qnaService = services?.QnAMakerService ?? throw new ArgumentNullException(nameof(services));

			// Set the telemetry client for this and all child dialogs.
			this.TelemetryClient = telemetryClient;

			// SubDialogs
			AddDialog(callHumanDialog);
			AddDialog(qnAMakerMultiturnDialog);
		}

		protected override async Task<DialogTurnResult> OnContinueDialogAsync(DialogContext innerDc, CancellationToken cancellationToken = default)
		{
			var result = await CheckIntentAsync(innerDc, cancellationToken).ConfigureAwait(false);
			if (result != null)
			{
				return result;
			}

			return await base.OnContinueDialogAsync(innerDc, cancellationToken).ConfigureAwait(false);
		}

		protected async Task<DialogTurnResult> CheckIntentAsync(DialogContext innerDc, CancellationToken cancellationToken)
		{
			// Se está rodandoo dialogo Qna, aumenta a pontuação mínima pra achar um Intent - pra nao confundir com as perguntas
			if (Utility.DialogIsRunning(innerDc, nameof(QnAMakerMultiturnDialog)))
				minScoreLuis = 0.70;

			// Confere se é atividade de texto, e o que foi digitado tem texto e não é um numero
			if (innerDc.Context.Activity.Type == ActivityTypes.Message && innerDc.Context.Activity.Text != null && !int.TryParse(innerDc.Context.Activity.Text, out int opcao))
			{
				// Ponteiro para os dados persistentes da conversa
				var conversationStateAccessors = _conversationState.CreateProperty<ConversationData>(nameof(ConversationData));
				var conversationData = await conversationStateAccessors.GetAsync(innerDc.Context, () => new ConversationData()).ConfigureAwait(false);

				// Se tinha algo salvo na primeira pergunta ( antes de rodar o Profile Dialog )
				if (!string.IsNullOrEmpty(conversationData.FirstQuestion) & ! Utility.DialogIsRunning(innerDc, nameof(ProfileDialog)))
					// Substitiu a pergunta do turno pela primeira pergunta que o cliente fez
					innerDc.Context.Activity.Text = conversationData.FirstQuestion;

				// Limpa ponto final, o ponto de exclamação , e o termo Susi ( atrapalha o LUIS )
				var userinput = Utility.CleanUtterance(innerDc.Context.Activity.Text);

				// Confere se não é em branco
				if (userinput.Length == 0)
					return null;

				innerDc.Context.Activity.Text = userinput;

				if (userinput == "cancelar" | userinput == "cancela" | userinput == "sair" | userinput == "voltar" | userinput == "menu" | userinput == "cancel" | userinput == "reiniciar" | userinput == "reiniciar conversa")
				{
					// Envia mensagem
					string text;
					if (userinput == "menu")
						text = "Ok, menu ...";
					else if (userinput == "sair")
						text = "Ok, saindo ...";
					else if (userinput == "voltar")
						text = "Ok, voltando ...";
					else
						text = "Ok, cancelando...";
					var message = MessageFactory.Text(text, text, InputHints.IgnoringInput);
					await innerDc.Context.SendActivityAsync(message, cancellationToken).ConfigureAwait(false);

					// Cancela todos os diálogos
					await innerDc.CancelAllDialogsAsync(cancellationToken).ConfigureAwait(false);

					// Limpa o que foi digitado
					innerDc.Context.Activity.Text = string.Empty;

					// E volta pro Menu Principal
					return await innerDc.ReplaceDialogAsync(nameof(AgendamentoDialog), null, cancellationToken).ConfigureAwait(false);
				}

                // Call LUIS and gather intent and any potential details
                try
                {
					var luisResult = await _recognizer.RecognizeAsync<MisterBotLuis>(innerDc.Context, cancellationToken).ConfigureAwait(false);
					// Salva os detalhes da intenção em ojbeto que será enviado pro Diálogo
					IntentDetails intentDetails = new IntentDetails { Intent = luisResult.TopIntent().intent };

					// Se a inteção tem numero ( quantidade ) copia pro objeto intentDetails
					if (luisResult.Entities.number != null) intentDetails.Quantidade = Utility.GetNumberFromLuis(luisResult);

					// Se a inteção tem vencimento, copia pro objeto intentDetails
					if (luisResult.Entities.datetime != null) intentDetails.DataSpec = luisResult.Entities.datetime[0];

					// Se a inteção tem atendente
					if (luisResult.Entities.atendente != null) intentDetails.Atendente = luisResult.Entities.atendente[0];

					// Verifica se o score da intenção ( a melhor pontuada ) tem pelo menos 0.5 de pontos ( menos que isso não esta correto )
					if (luisResult.TopIntent().score > minScoreLuis)
					{
						// Obrigado
						if (luisResult.TopIntent().intent == MisterBotLuis.Intent.obrigado & !Utility.DialogIsRunning(innerDc, nameof(ProfileDialog)))
						{
							// Language Generation message: De nada!
							string lgOutput = _lgTemplates.Evaluate("DeNada", null).ToString();
							await innerDc.Context.SendActivityAsync(MessageFactory.Text(lgOutput, lgOutput, InputHints.ExpectingInput), cancellationToken).ConfigureAwait(false);

							// Limpa a primeira frase digitada no dialogo
							conversationData.FirstQuestion = string.Empty;

							// E continua o turno
							return new DialogTurnResult(DialogTurnStatus.Waiting);
						}

						// Saudação - exceto se for a primeira pergunta da conversa ou já estiver dentro do agendamento
						else if (luisResult.TopIntent().intent == MisterBotLuis.Intent.saudacao & string.IsNullOrEmpty(conversationData.FirstQuestion) & !Utility.DialogIsRunning(innerDc, nameof(AgendamentoDialog)))
						{
							// Language Generation message: Como posso Ajudar?
							string lgOutput = _lgTemplates.Evaluate("ComoPossoAjudar", new { userName = conversationData.Customer != null ? Utility.FirstName(conversationData.Customer.Name) : string.Empty }).ToString();
							await innerDc.Context.SendActivityAsync(MessageFactory.Text(lgOutput, lgOutput, InputHints.ExpectingInput), cancellationToken).ConfigureAwait(false);

							// Limpa a primeira frase digitada no dialogo
							conversationData.FirstQuestion = string.Empty;

							// E continua o turno
							return new DialogTurnResult(DialogTurnStatus.Waiting);
						}

						//// Falar com atendente
						//else if (luisResult.TopIntent().intent == MisterBotLuis.Intent.falar_com_atendente)
						//{
						//	if (intentDetails.Atendente != null)
						//		mensagem = "Certo, falar com " + intentDetails.Atendente;
						//	else
						//		mensagem = "Tudo bem, falar com um atendente ...";
						//	await innerDc.Context.SendActivityAsync(MessageFactory.Text(mensagem), cancellationToken).ConfigureAwait(false);

						//	// Limpa a primeira frase digitada no dialogo
						//	conversationData.FirstQuestion = string.Empty;

						//	// Cancela os dialogos atuais
						//	await innerDc.CancelAllDialogsAsync().ConfigureAwait(false);

						//	// Dispara o diálogo pra falar com atendente
						//	return await innerDc.BeginDialogAsync(nameof(CallHumanDialog), intentDetails, cancellationToken).ConfigureAwait(false);
						//}

					}

					//// QNA
					//// Confere se o que foi digitado tem pelo menos 3 palavras, e se não está dentro do diálogo "QnADialog"
					//if (userinput.Split(" ").GetLength(0) > 2 && !Utility.DialogIsRunning(innerDc, nameof(QnAMakerMultiturnDialog)) & string.IsNullOrEmpty(conversationData.FirstQuestion))
					//{
					//	try
					//	{
					//		// Calling QnAMaker to get response.
					//		var qnaResponses = await _qnaService.GetAnswersAsync(innerDc.Context).ConfigureAwait(false);

					//		// Se achou alguma resposta
					//		if (qnaResponses.Any() && qnaResponses.First().Score > minScoreQna)
					//		{
					//			// Limpa a primeira frase digitada no dialogo
					//			conversationData.FirstQuestion = string.Empty;

					//			// Chama o QnaMakerMultiturnDialog
					//			return await Utility.CallQnaDialog(innerDc, cancellationToken).ConfigureAwait(false);
					//		}

					//	}
					//	catch ( Exception ex)
					//	{
					//		_logger.LogError(ex.Message);
					//	}
					//}

					//// Confere se tem numero escrito por extenso - nao roda nas perguntas de data
					//else if (!Utility.DialogIsRunning(innerDc, "ProfileDialog") && !Utility.DialogIsRunning(innerDc, "ProfileDialog3") && !Utility.DialogIsRunning(innerDc, "ProfileDialog2") && !Utility.DialogIsRunning(innerDc, "AskAccount") && !Utility.DialogIsRunning(innerDc, "DueDate") && luisResult.TopIntent().intent == MisterBotLuis.Intent.None & luisResult.Entities.number != null)
					//{
					//	// coloca no texto o numero digitado
					//	innerDc.Context.Activity.Text = luisResult.Entities.number[0].ToString(CultureInfo.InvariantCulture);
					//}

					// Versao
					else if (innerDc.Context.Activity.Text == "versao")
					{
						// Language Generation message: Como posso Ajudar?
						string lgOutput = _lgTemplates.Evaluate("Versao").ToString();
						await innerDc.Context.SendActivityAsync(MessageFactory.Text(lgOutput, lgOutput, InputHints.ExpectingInput), cancellationToken).ConfigureAwait(false);

						// Limpa a primeira frase digitada no dialogo
						conversationData.FirstQuestion = string.Empty;

						// E continua o turno
						return new DialogTurnResult(DialogTurnStatus.Waiting);
					}

				}
				catch (Exception)
                {
					return null;
                }
			}

			// Se veio anexo, e não está no diálogo de enviar comprovante
			else if ( innerDc.Context.Activity.Attachments != null & !Utility.DialogIsRunning(innerDc, "ComprovanteDialog"))
			{
				// Salva o anexo
				await Utility.SaveAttachmentAsync(innerDc.Context, _blobContainerClient).ConfigureAwait(false);

				// E continua o turno
				return new DialogTurnResult(DialogTurnStatus.Waiting);
			}

			// Se chegou até aqui, devolve null
			return null;
		}

	}
}
