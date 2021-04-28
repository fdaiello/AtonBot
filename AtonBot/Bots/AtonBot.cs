// Mister Bot
// Base do Bot da Mister Postman
//

using Azure.Storage.Blobs;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Configuration;
using ContactCenter.Data;
using MrBot.Dialogs;
using ContactCenter.Core.Models;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PloomesApi;
using Microsoft.Extensions.Logging;
using ContactCenter.Infrastructure.Clients.Wpush;

namespace MrBot.Bots
{
	public class AtonBot : ActivityHandler
	{
		// Conversation state - used by Dialog system
		private readonly ConversationState _conversationState;

		// Database context 
		private readonly ApplicationDbContext _botDbContext;

		// Dependency injected dictionary for storing ConversationReference objects used in NotifyController to proactively message users
		private readonly ConcurrentDictionary<string, ConversationReference> _conversationReferences;

		// Bot HTTP Adapter
		private readonly IBotFrameworkHttpAdapter _adapter;

		// Root Dialog - o dialog inicial do Bot
		private readonly RootDialog _rootdialog;

		// Para enviar notificações por Push
		private readonly WpushClient _wpushClient;

		// Configurações gerais
		private readonly IConfiguration _configuration;

		// Cliente para salvar arquivos na nuvem
		private readonly BlobContainerClient _blobContainerClient;

		// Dados do cliente
		private readonly Contact _customer;

		// Cliente para acessar a api do Ploomes
		private readonly PloomesClient _ploomesclient;

		// Ploomes Contact
		private readonly PloomesContact _contact;

		// Ploomes Deal
		private readonly Deal _deal;

		// Logger
		private readonly ILogger _logger;

		// Mr Bot Class Constructor
		public AtonBot(ConversationState conversationState, ApplicationDbContext botContext, ConcurrentDictionary<string, ConversationReference> conversationReferences, IBotFrameworkHttpAdapter adapter, RootDialog rootdialog, WpushClient wpushClient, IConfiguration configuration, BlobContainerClient blobContainerClient, Contact customer, PloomesClient ploomesClient, Deal deal, PloomesApi.PloomesContact contact, ILogger<AtonBot> logger)
		{
			// Injected objects
			_conversationState = conversationState;
			_conversationReferences = conversationReferences;
			_adapter = adapter;
			_rootdialog = rootdialog;
			_botDbContext = botContext;
			_wpushClient = wpushClient;
			_configuration = configuration;
			_blobContainerClient = blobContainerClient;
			_customer = customer;
			_ploomesclient = ploomesClient;
			_contact = contact;
			_deal = deal;
			_logger = logger;
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
			// Hora no Brasil
			DateTime horalocal = Utility.HoraLocal();

            // Consulta se o usuário já está cadastrado na base do Bot
            Contact customer = _botDbContext.Contacts
						.Where(s => s.Id == Id)
						.FirstOrDefault();

			// Se ja tem registro na base
			if (customer != null)
			{
				// Copia os dados para o _customer que será compartilhado com as outras classes
				_customer.CopyFrom(customer);

				// Verifica se tem salvo na base local o ID do cliente salvo no Ploomes
				if (!string.IsNullOrEmpty(_customer.Tag1) && int.TryParse(_customer.Tag1, out int ploomesClientId))
				{
                    // Verifica se ja tem um Contact ( contato ) salvo para este Cliente
                    PloomesApi.PloomesContact contact = await _ploomesclient.GetContact(ploomesClientId).ConfigureAwait(false);

					// Copia os dados do Contact para o objeto injetado, compartilhado entre as classes
					_contact.CopyFrom(contact);

					// Verifica se já tem um Deal ( Negócio ) salvo para este Cliente
					Deal deal = await _ploomesclient.GetDeal(ploomesClientId).ConfigureAwait(false);

					// Copia os dados do Deal para o Deal injetada, compartilhada entre as classes
					_deal.CopyFrom(deal);
				}

				// Se já faz mais de 24 horas que teve atividade
				if (horalocal.Subtract(customer.LastActivity).TotalHours > 24 )
				{
					if (_conversationState != null)
					{
						try
						{
							// Delete the conversationState for the current conversation to prevent the
							// bot from getting stuck in a error-loop caused by being in a bad state.
							// ConversationState should be thought of as similar to "cookie-state" in a Web pages.
							await _conversationState.DeleteAsync(turnContext).ConfigureAwait(false);
						}
						catch (Exception e)
						{
							_logger.LogError(e, $"Exception caught on attempting to Delete ConversationState : {e.Message}");
						}
					}
					return false;
				}
			}

			// Se já está na base, e está falando com um agente
			if (customer != null && customer.Status == ContactStatus.TalkingToAgent)
			{

				// Salva o arquivo
				string filename = await Utility.SaveAttachmentAsync(turnContext, _blobContainerClient).ConfigureAwait(false);

				// Se já faz mais de 6 horas que teve atividade com atendente
				if (horalocal.Subtract(customer.LastActivity).TotalHours > 5)
				{
					// Resseta o status do cliente
					customer.Status = ContactStatus.TalkingToBot;
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
							await _wpushClient.SendNotification(customer.Name, turnContext.Activity.Text, _configuration.GetValue<string>($"ChatUrl"), applicationUser.WebPushId).ConfigureAwait(false);
						}
					}
					else
						// Sends WebPush Notification for a single Agent - the one associated with this Customer
						await _wpushClient.SendNotification(customer.Name, turnContext.Activity.Text, _configuration.GetValue<string>($"ChatUrl"), webPushId).ConfigureAwait(false);

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
