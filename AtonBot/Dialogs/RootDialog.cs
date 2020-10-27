// MrBot 2020
// Root Dialog

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.LanguageGeneration;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Bot.Schema;
using Microsoft.Bot.Builder.AI.QnA;
using Azure.Storage.Blobs;
using MrBot.Data;
using MrBot.Models;
using MrBot.CognitiveModels;
using PloomesApi;
using System.Globalization;

namespace MrBot.Dialogs
{
	public class RootDialog : CheckIntentBase
	{
		private readonly BotDbContext _botDbContext;
		private readonly DialogDictionary _dialogDictionary;
		private readonly ConversationState _conversationState;
		private readonly ILogger _logger;
		private readonly QnAMaker _qnaService;
		private readonly double minScoreQna = 0.5;
		private readonly Models.Contact _customer;
		private readonly Deal _deal;
		private readonly PloomesClient _ploomesClient;

		public RootDialog(ConversationState conversationState, BotDbContext botContext, DialogDictionary dialogDictionary, Models.Contact customer, Deal deal, ProfileDialog profileDialog, MisterBotRecognizer recognizer, CallHumanDialog callHumanDialog, IBotTelemetryClient telemetryClient, Templates lgTemplates, BlobContainerClient blobContainerClient, ILogger<RootDialog> logger, IQnAMakerConfiguration services, QnAMakerMultiturnDialog qnAMakerMultiturnDialog, AgendaVisitaDialog agendaVisitaDialog, ReAgendaVisitaDialog reagendaVisitaDialog, EnviaPropostaDialog enviaPropostaDialog, AgendaInstalacaoDialog agendaInstalacaoDialog, QuerAtendimentoDialog querAtendimentoDialog, PloomesClient ploomesClient)
			: base(nameof(RootDialog), conversationState, recognizer, callHumanDialog, telemetryClient, lgTemplates, blobContainerClient, logger, services, qnAMakerMultiturnDialog, customer)
		{
			// Injected objects
			_botDbContext = botContext;
			_dialogDictionary = dialogDictionary;
			_conversationState = conversationState;
			_logger = logger;
			_qnaService = services?.QnAMakerService ?? throw new ArgumentNullException(nameof(services));
			_customer = customer;
			_deal = deal;
			_ploomesClient = ploomesClient;

			// Set the telemetry client for this and all child dialogs.
			this.TelemetryClient = telemetryClient;

			// Adiciona os subdialogos
			AddDialog(profileDialog);
			AddDialog(agendaVisitaDialog);
			AddDialog(reagendaVisitaDialog);
			AddDialog(enviaPropostaDialog);
			AddDialog(agendaInstalacaoDialog);
			AddDialog(querAtendimentoDialog);

			// Array com a lista de métodos que este WaterFall Dialog vai executar.
			var waterfallSteps = new WaterfallStep[]
			{
				CheckCustomerAndCallProfileDialogStepAsync,
				//CheckNCallQnAStepAsync,
				CheckStageAndCallNextDialogStepAsync
			};

			// Adiciona um diálogo do tipo WaterFall a este conjunto de diálogos, com os passos listados acima
			AddDialog(new WaterfallDialog(nameof(WaterfallDialog), waterfallSteps));

			// Configura este diálogo para iniciar rodando o WaterFall Dialog definido acima
			InitialDialogId = nameof(WaterfallDialog);
		}

		// Verifica se este cliente já esta cadastrado na nossa base
		// Se ainda não tem cadastro, chama o ProfileDialog
		// Se já tem cadastro, pula para o proximo passo, que chama o Menu Principal
		private async Task<DialogTurnResult> CheckCustomerAndCallProfileDialogStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{

			// Ponteiro para os dados persistentes da conversa
			var conversationStateAccessors = _conversationState.CreateProperty<ConversationData>(nameof(ConversationData));
			var conversationData = await conversationStateAccessors.GetAsync(stepContext.Context, () => new ConversationData()).ConfigureAwait(false);

			// Se o usuario ainda não esta na base do Bot ( ou se está mas deu algum problema, e não completou Profile Dialog, e nao tem nome )
			if (_customer == null || _customer.Id == null)
			{

				// Cria um registro para este novo usuario
				await CreateCustomer(stepContext.Context.Activity).ConfigureAwait(false);

				// Chama o Diálogo que preenche e salva o perfil do usuário no banco de dados do Bot
				return await stepContext.BeginDialogAsync(nameof(ProfileDialog), null, cancellationToken).ConfigureAwait(false);
			}

			// Se o usuario já esta no banco do Bot
			else
			{

				// Confere se tem inteção digitada - e processa
				var result = await base.CheckIntentAsync(stepContext, cancellationToken).ConfigureAwait(false);

				// Limpa a primeira frase digitada no dialogo
				conversationData.FirstQuestion = string.Empty;

				// Se fez algo baseado em inteção digitada
				if (result != null)
                {
					// Retorna com o resultado do que foi feito - encerra por aqui
					return result;
				}
				else
				{
					// Language Generation message: Como posso Ajudar?
					string text = _customer != null ? "Oi " + Utility.FirstName(_customer.Name) : "Oi.";
					await stepContext.Context.SendActivityAsync(MessageFactory.Text(text), cancellationToken).ConfigureAwait(false);

					// Vai para o proximo passo, que chama o menu Principal - passa como null a resposta deste passo
					return await stepContext.NextAsync(null, cancellationToken).ConfigureAwait(false);
				}
			}

		}

		// Confere se a frase tem resposta em QnA
		private async Task<DialogTurnResult> CheckNCallQnAStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{
			// Calling QnAMaker to get response.
			var qnaResponses = await _qnaService.GetAnswersAsync(stepContext.Context).ConfigureAwait(false);

			// Se achou alguma resposta
			if (qnaResponses.Any() && qnaResponses.First().Score > minScoreQna)
			{
				// Ponteiro para os dados persistentes da conversa
				var conversationStateAccessors = _conversationState.CreateProperty<ConversationData>(nameof(ConversationData));
				var conversationData = await conversationStateAccessors.GetAsync(stepContext.Context, () => new ConversationData()).ConfigureAwait(false);

				// Salva a primeira pergunta que o cliente fez
				conversationData.FirstQuestion = string.Empty;

				// Chama o QnaMakerMultiturnDialog
				return await Utility.CallQnaDialog(stepContext, cancellationToken).ConfigureAwait(false);
			}
			else
				// Vai para o proximo passo, que chama o menu Principal - passa como null a resposta deste passo
				return await stepContext.NextAsync(null, cancellationToken).ConfigureAwait(false);

		}
		// Confere o estágio do Deal no Ploomes, e chama o proximo diálogo conforme o estágio;
		private async Task<DialogTurnResult> CheckStageAndCallNextDialogStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{

			// Se ainda nao tem Deal salvo
			if (_deal.ContactId==0)
				// Chama o diálogo do agendamento da Visita
				return await stepContext.BeginDialogAsync(nameof(AgendaVisitaDialog), null, cancellationToken).ConfigureAwait(false);

			// Se esta nos estagios Nulo, Lead ( o lead já é slavo com a visita agendada ) ou Visita Agendada
			else if(_deal.StageId == AtonStageId.Nulo || _deal.StageId == AtonStageId.Lead || _deal.StageId == AtonStageId.VisitaAgendada){
				// Chama o diálogo do reagendamento da Visita
				return await stepContext.BeginDialogAsync(nameof(ReAgendaVisitaDialog), null, cancellationToken).ConfigureAwait(false);
			}
			else if ( _deal.StageId == AtonStageId.VisitaRealizada )
            {
				await stepContext.Context.SendActivityAsync(MessageFactory.Text("No meu sistema consta que sua visita técnica já foi realizada, mas ainda não tenho sua proposta. Por favor, aguarde."), cancellationToken).ConfigureAwait(false);
			}
			else if ( _deal.StageId == AtonStageId.PropostaRealizada)
            {
				await stepContext.Context.SendActivityAsync(MessageFactory.Text("Estamos trabalhando na sua proposta, e em breve vamos lhe enviar."), cancellationToken).ConfigureAwait(false);
			}
			else if (_deal.StageId == AtonStageId.ValidacaoDaVisitaeProposta || _deal.StageId == AtonStageId.PropostaApresentada)
			{
				// Confere se ja tem o resultado da validacão, e se o resultado é Validada
				if (_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.ResultadoValidacao).Any() && Int32.TryParse(Convert.ToString(_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.ResultadoValidacao).FirstOrDefault().IntegerValue,CultureInfo.InvariantCulture), out int resultadoValicao) && resultadoValicao == AtonResultadoValicacao.Validada)
					// Chama o diálogo do segundo
					return await stepContext.BeginDialogAsync(nameof(EnviaPropostaDialog), null, cancellationToken).ConfigureAwait(false);
				else
					await stepContext.Context.SendActivityAsync(MessageFactory.Text("Sua proposta está pronta, só falta nossa equipe validar. Por favor, aguarde!"), cancellationToken).ConfigureAwait(false);
			}
			else if (_deal.StageId == AtonStageId.PropostaAceita)
            {
				// Confere se já enviou o comprovante
				if (_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.Comprovante1aParcelaIdentificado).Any() && _deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.Comprovante1aParcelaIdentificado).FirstOrDefault().BoolValue != null && (bool)_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.Comprovante1aParcelaIdentificado).FirstOrDefault().BoolValue )
					// chama diálogo de agendamento da instalação
					return await stepContext.BeginDialogAsync(nameof(AgendaInstalacaoDialog), null, cancellationToken).ConfigureAwait(false);

				else
					// Pede para enviar comprovante por email
					await stepContext.Context.SendActivityAsync(MessageFactory.Text($"Por favor, envie o comprovante de pagamento da primeira parcela por email para comprovante@atonservices.com.br. Os dados para pagamentos estão indicados na proposta. Assim que recebermos o comprovante, poderemos agendar a instalação."), cancellationToken).ConfigureAwait(false);

			}
			else if ( _deal.StageId == AtonStageId.InstalacaoAgendada)
            {
				// chama diálogo de agendamento da instalação
				return await stepContext.BeginDialogAsync(nameof(AgendaInstalacaoDialog), null, cancellationToken).ConfigureAwait(false);
			}
			else if(_deal.StageId == AtonStageId.InstalacaoEmExecucao)
			{

				// Ponteiro para os dados persistentes da conversa
				var conversationStateAccessors = _conversationState.CreateProperty<ConversationData>(nameof(ConversationData));
				var conversationData = await conversationStateAccessors.GetAsync(stepContext.Context, () => new ConversationData()).ConfigureAwait(false);

				// Confere se tem boleto, e ainda não enviou
				if (!conversationData.BoletoEnviado && _deal.OtherProperties != null && _deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.BoletoAttachmentId).Any() && _deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.BoletoAttachmentId).FirstOrDefault().IntegerValue != null && (long)_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.BoletoAttachmentId).FirstOrDefault().IntegerValue > 0)
				{
					// pega o Id do Attachmento do Boleto
					long boletoAttId = (long)_deal.OtherProperties.Where(p => p.FieldKey == DealPropertyId.BoletoAttachmentId).FirstOrDefault().IntegerValue;
					// busca o Attachment do Boleto
					PloomesAttachment attachment = await _ploomesClient.GetAttachment(boletoAttId).ConfigureAwait(false);
					// confere se achou
					if (attachment != null && !string.IsNullOrEmpty(attachment.Url))
					{
						// Envia o anexo
						await Utility.EnviaAnexo(stepContext, "Boleto", "O pessoal do Financeiro já me passou o seu boleto de pagamento da segunda parcela. Já vou lhe enviar...", attachment.Url, attachment.ContentType, cancellationToken).ConfigureAwait(false);

						// Espera pra dar tempo da mensagem carregar, e não chegar depois da proxima mensagem
						Task.Delay(3000).Wait();

						// Marca no ojeto persistente da conversa, que já enviou o boleto
						conversationData.BoletoEnviado = true;

						// E encerra o diálogo
						return await stepContext.EndDialogAsync().ConfigureAwait(false);
					}
				}

				await stepContext.Context.SendActivityAsync(MessageFactory.Text("No meu sistema, consta que sua instalação está em andamento. Por favor, aguarde."), cancellationToken).ConfigureAwait(false);
			}
            else
            {
				// Informa que a instalaçao foi finalizada
				await stepContext.Context.SendActivityAsync(MessageFactory.Text("No meu sistema, consta que sua instalação já foi finalizada."), cancellationToken).ConfigureAwait(false);

				// chama diálogo se quer falar com um humano
				return await stepContext.BeginDialogAsync(nameof(QuerAtendimentoDialog), null, cancellationToken).ConfigureAwait(false);
			}

			return await stepContext.EndDialogAsync().ConfigureAwait(false);

		}

		// Insere um novo registro para este usuario
		private async Task CreateCustomer(Activity activity)
		{

			// Busca o grupo no qual o usuario deve ser inserido - conforme nome do Bot - appName
			int groupId = await GetGroupId(activity).ConfigureAwait(false);

			// Id do usuario
			string clientId = activity.From.Id;
			// ChannelId
			string channelID = activity.ChannelId;

			// Mapeia o canal
			ChannelType channelType;
			if (channelID == "whatsapp")
				channelType = ChannelType.WhatsApp;
			else if (channelID == "webchat")
				channelType = ChannelType.WebChat;
			else if (channelID == "facebook")
				channelType = ChannelType.Facebook;
			else
				channelType = ChannelType.others;
			
			try
			{
                // Cria um novo cliente
                Models.Contact customer = new Models.Contact
				{
					Id = clientId,
					FirstActivity = Utility.HoraLocal(),
					LastActivity = Utility.HoraLocal(),
					GroupId = groupId,
					ChannelType = channelType,
					Channel = string.Empty,
				};

				//Insere o cliente no banco
				_botDbContext.Contacts.Add(customer);
				await _botDbContext.SaveChangesAsync().ConfigureAwait(false);

				// Copia pra variavel injetada compartilhada entre as classes
				_customer.CopyFrom(customer);

			}
			catch ( Exception ex)
			{
				_logger.LogError(ex.Message);
				if (ex.InnerException != null)
					_logger.LogError(ex.InnerException.Message);
			}
		}
		// Busca o grupo no qual o usuário deve ser inserido - com base na identificação do Bot
		private async Task<int> GetGroupId(Activity activity)
		{
			// Busca a identificação do Bot
			string appName = activity.Recipient.Id;
			if (activity.ChannelId == "webchat")
				appName = activity.From.Name;

			// Busca o Grupo no banco
			Group groups = await _botDbContext.Groups.Where(p => p.BotName == appName).FirstOrDefaultAsync().ConfigureAwait(false);

			if (groups == null)
				return 1;
			else
				return groups.Id;

		}
	}
}
