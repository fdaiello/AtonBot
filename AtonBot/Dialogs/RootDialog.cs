﻿// MrBot 2020
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
		private readonly Customer _customer;

		public RootDialog(ConversationState conversationState, BotDbContext botContext, DialogDictionary dialogDictionary, ProfileDialog profileDialog, MisterBotRecognizer recognizer, CallHumanDialog callHumanDialog, IBotTelemetryClient telemetryClient, Templates lgTemplates, BlobContainerClient blobContainerClient, ILogger<RootDialog> logger, IQnAMakerConfiguration services, QnAMakerMultiturnDialog qnAMakerMultiturnDialog, AgendamentoDialog agendamentoDialog, Customer customer)
			: base(nameof(RootDialog), conversationState, recognizer, callHumanDialog, telemetryClient, lgTemplates, blobContainerClient, logger, services, qnAMakerMultiturnDialog, customer)
		{
			// Injected objects
			_botDbContext = botContext;
			_dialogDictionary = dialogDictionary;
			_conversationState = conversationState;
			_logger = logger;
			_qnaService = services?.QnAMakerService ?? throw new ArgumentNullException(nameof(services));
			_customer = customer;

			// Set the telemetry client for this and all child dialogs.
			this.TelemetryClient = telemetryClient;

			// Adiciona os subdialogos
			AddDialog(profileDialog);
			AddDialog(agendamentoDialog);

			// Array com a lista de métodos que este WaterFall Dialog vai executar.
			var waterfallSteps = new WaterfallStep[]
			{
				CheckAndCallProfileDialogStepAsync,
				//CheckNCallQnAStepAsync,
				CallAgendamentoDialogStepAsync
			};

			// Adiciona um diálogo do tipo WaterFall a este conjunto de diálogos, com os passos listados acima
			AddDialog(new WaterfallDialog(nameof(WaterfallDialog), waterfallSteps));

			// Configura este diálogo para iniciar rodando o WaterFall Dialog definido acima
			InitialDialogId = nameof(WaterfallDialog);
		}

		// Verifica se este cliente já esta cadastrado na nossa base
		// Se ainda não tem cadastro, chama o ProfileDialog
		// Se já tem cadastro, pula para o proximo passo, que chama o Menu Principal
		private async Task<DialogTurnResult> CheckAndCallProfileDialogStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{

			// Ponteiro para os dados persistentes da conversa
			var conversationStateAccessors = _conversationState.CreateProperty<ConversationData>(nameof(ConversationData));
			var conversationData = await conversationStateAccessors.GetAsync(stepContext.Context, () => new ConversationData()).ConfigureAwait(false);

			// Se o usuario ainda não esta na base do Bot ( ou se está mas deu algum problema, e não completou Profile Dialog, e nao tem nome )
			if (_customer == null)
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
					// Retorna com o resultado do que foi feito - encerra por aqui
					return result;
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
		// Chama o Diálogo do Agendamento
		private async Task<DialogTurnResult> CallAgendamentoDialogStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{

			// Chama o diálogo do primeiro agendamento
			return await stepContext.BeginDialogAsync(nameof(AgendamentoDialog), null, cancellationToken).ConfigureAwait(false);
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
				Customer customer = new Customer
				{
					Id = clientId,
					FirstActivity = Utility.HoraLocal(),
					LastActivity = Utility.HoraLocal(),
					GroupId = groupId,
					ChannelType = channelType,
					Channel = string.Empty,
				};

				//Insere o cliente no banco
				_botDbContext.Customers.Add(customer);
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
