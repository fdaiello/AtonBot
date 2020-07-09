﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
//

using Azure.Storage.Blobs;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Configuration;
using MrBot.Data;
using MrBot.Dialogs;
using MrBot.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MrBot.Bots
{
	public class MisterBot : ActivityHandler
	{
		// Conversation state - used by Dialog system
		private readonly ConversationState _conversationState;

		// Database context 
		private readonly BotDbContext _botDbContext;

		// Dependency injected dictionary for storing ConversationReference objects used in NotifyController to proactively message users
		private readonly ConcurrentDictionary<string, ConversationReference> _conversationReferences;

		// Bot HTTP Adapter
		private readonly IBotFrameworkHttpAdapter _adapter;

		// Root Dialog - o dialog inicial do Bot
		private readonly RootDialog _rootdialog;

		// Para enviar notificações por Push
		private readonly WpushApi _wpushApi;

		// Configurações gerais
		private readonly IConfiguration _configuration;

		// Cliente para salvar arquivos na nuvem
		private readonly BlobContainerClient _blobContainerClient;

		// Mr Bot Class Constructor
		public MisterBot(ConversationState conversationState, BotDbContext botContext, ConcurrentDictionary<string, ConversationReference> conversationReferences, IBotFrameworkHttpAdapter adapter, RootDialog rootdialog, WpushApi wpushApi, IConfiguration configuration, BlobContainerClient blobContainerClient)
		{
			// Injected objects
			_conversationState = conversationState;
			_conversationReferences = conversationReferences;
			_adapter = adapter;
			_rootdialog = rootdialog;
			_botDbContext = botContext;
			_wpushApi = wpushApi;
			_configuration = configuration;
			_blobContainerClient = blobContainerClient;
		}

		// Metodo executado a cada mensagem recebida
		protected override async Task OnMessageActivityAsync(ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
		{
			// Update Conversation Reference
			UpdateConversationReference(turnContext);

			// Confere se não está em atendimento humano
			bool isTalkingToAgent = await IsTalkingToAgent(turnContext.Activity.From.Id, turnContext).ConfigureAwait(false);
			if (!isTalkingToAgent)
				// Roda o Diálogo Raiz - RootDialog
				await _rootdialog.RunAsync(turnContext, _conversationState.CreateProperty<DialogState>(nameof(DialogState)), cancellationToken).ConfigureAwait(false);
		}

		// Confere se o cliente está falando com um agente
		protected async Task<bool> IsTalkingToAgent(string Id, ITurnContext<IMessageActivity> turnContext)
		{
			// Consulta se o usuário já está cadastrado na base do Bot
			Customer customer = _botDbContext.Customers
						.Where(s => s.Id == Id)
						.FirstOrDefault();

			// Se já está na base, e está falando com um agente
			if (customer != null && customer.Status == CustomerStatus.TalkingToAgent)
			{

				// Salva o arquivo
				string filename = await Utility.SaveAttachmentAsync(turnContext, _blobContainerClient).ConfigureAwait(false);

				// Se já faz mais de 30 minutos que teve atividade ( com agente )
				DateTime horalocal = Utility.HoraLocal();
				if (horalocal.Subtract(customer.LastActivity).TotalMinutes > 30)
				{
					// Resseta o status do cliente
					customer.Status = CustomerStatus.TalkingToBot;
					_botDbContext.Update(customer);
					await _botDbContext.SaveChangesAsync().ConfigureAwait(false);

					return false;
				}
				// Se faz mais de 2 minutos da ultima interação
				else if (horalocal.Subtract(customer.LastActivity).TotalMinutes > 2)
				{

					string webPushId = null;

					// If Customer has an Agent associated with it - one who is attending him
					if (!string.IsNullOrEmpty(customer.ApplicationUserId))
					{

						// Search for Agent Record
						ApplicationUser applicationUser = _botDbContext.ApplicationUsers
														.Where(s => s.Id == customer.ApplicationUserId)
														.FirstOrDefault();

						// If Agent has WebPush Subscriber ID saved to database
						if (applicationUser != null && !string.IsNullOrEmpty(applicationUser.WebPushId))
							// Saves webpushId from Agent who attends this Customer
							webPushId = applicationUser.WebPushId;
					}

					// If there is no associated Agent to this customer, or associated Agent has no WebPushId ...
					if ( webPushId == null )
					{
						// Sends WebPush Notification for all Agents of the Customer Group
						IQueryable<ApplicationUser> applicationUsers = _botDbContext.ApplicationUsers
																.Where(p => p.GroupId == customer.GroupId && ! string.IsNullOrEmpty(p.WebPushId));
						foreach ( ApplicationUser applicationUser in applicationUsers)
						{
							// Sends WebPush Notification for this Agent
							await _wpushApi.SendNotification(customer.Name, turnContext.Activity.Text, _configuration.GetValue<string>($"ChatUrl"), applicationUser.WebPushId).ConfigureAwait(false);
						}
					}
					else
						// Sends WebPush Notification for a single Agent - the one associated with this Customer
						await _wpushApi.SendNotification(customer.Name, turnContext.Activity.Text, _configuration.GetValue<string>($"ChatUrl"), webPushId).ConfigureAwait(false);

				}

				return true;
			}
			else
				return false;

		}

		// Roda a cada inicio de Conversa
		protected override Task OnConversationUpdateActivityAsync(ITurnContext<IConversationUpdateActivity> turnContext, CancellationToken cancellationToken)
		{

			// Grava em um array em memoria os ConversationReference do usuario - necessário para enviar mensagem proativa direta se precisar

			var conversationReference = turnContext.Activity.GetConversationReference();
			_conversationReferences.AddOrUpdate(conversationReference.User.Id, conversationReference, (key, newValue) => conversationReference);

			return base.OnConversationUpdateActivityAsync(turnContext, cancellationToken);

		}

		// Roda no final de cada turno
		public override async Task OnTurnAsync(ITurnContext turnContext, CancellationToken cancellationToken = default)
		{
			// Executa o próprio método da classe mae
			await base.OnTurnAsync(turnContext, cancellationToken).ConfigureAwait(false);

			// Salva o estado da conversa
			await _conversationState.SaveChangesAsync(turnContext, false, cancellationToken).ConfigureAwait(false);

		}
		private void UpdateConversationReference(ITurnContext turnContext)
		{
			// Testa se a chave existe no dicionário
			if (!_conversationReferences.ContainsKey(turnContext.Activity.From.Id))
			{
				// Grava em um array em memoria os ConversationReference do usuario - necessário para enviar mensagem proativa direta se precisar
				var conversationReference = turnContext.Activity.GetConversationReference();
				_conversationReferences.AddOrUpdate(conversationReference.User.Id, conversationReference, (key, newValue) => conversationReference);
			}
			return;
		}

	}
}
