// MrBot 2020
// Diálogo para chamar um atendente humano
using Correios;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Extensions.Configuration;
using MrBot.Data;
using MrBot.Models;
using NETCore.MailKit.Core;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MrBot.Dialogs
{
	public class CallHumanDialog : ComponentDialog
	{
		private readonly DialogDictionary _dialogDictionary;
		private readonly BotDbContext _botDbContext;
		private readonly WpushApi _wpushApi;
		private readonly IEmailService _emailService;
		private readonly IConfiguration _configuration;

		public CallHumanDialog(BotDbContext botDbContext, DialogDictionary dialogDictionary, WpushApi wpushApi, IEmailService emailService, IConfiguration configuration, IBotTelemetryClient telemetryClient, ProfileDialog2 profileDialog2)
			: base(nameof(CallHumanDialog))
		{

			// Injected objects
			_dialogDictionary = dialogDictionary;
			_botDbContext = botDbContext;
			_wpushApi = wpushApi;
			_emailService = emailService;
			_configuration = configuration;

			// Set the telemetry client for this and all child dialogs.
			this.TelemetryClient = telemetryClient;

			// Adiciona um diálogo de prompt de texto sem validacao
			AddDialog(new TextPrompt(nameof(TextPrompt)));

			// Adiciona um diálogo WaterFall com os passos ( métodos ) que serão executados.
			AddDialog(new WaterfallDialog(nameof(WaterfallDialog), new WaterfallStep[]
			{
				Profile2StepAsync,
				CheckWorkingHoursStepAsync,
				OutOfWorkingHoursStepAsync
			}));

			// Adiciona subdialogo
			AddDialog(profileDialog2);

			// Configura este diálogo para iniciar rodando o WatefallDialog criado acima.
			InitialDialogId = nameof(WaterfallDialog);

		}

		// Confere se o cadastro está completo
		private async Task<DialogTurnResult> Profile2StepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{

			// Chama o diálogo que seleciona uma conta, procura uma conta, ou cria uma nova conta
			return await stepContext.BeginDialogAsync(nameof(ProfileDialog2), null, cancellationToken).ConfigureAwait(false);
		}

		// Se dentro do horario de expediente
		//     Envia notificação WebPush pros Agentes
		//     Marca o status do cliente como Wating
		//     Envia aviso pro cliente que notificou, pede para aguardar, envie algo pra continuar
		//     Finaliza o diálogo
		// Se fora do horario de expediente
		//     Avisa que está fora do horario comercial
		//     Pergunta o horário de preferencia do usuário pra receber o contato
		private async Task<DialogTurnResult> CheckWorkingHoursStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{
			// Confere se especificou um atendente
			string atendente = string.Empty;
			var intentDetails = (IntentDetails)stepContext.Options;
			if (intentDetails != null && !string.IsNullOrEmpty(intentDetails.Atendente))
				atendente = intentDetails.Atendente;
			else
				atendente = "um atendente";

			stepContext.Values["atendente"] = atendente;

			if (InWorkingHours())
			{
				// Marca que esta dentro de InWorkingHours
				stepContext.Values["inworkinghurs"] = "sim";


				// Marca cliente com status Wating
				Customer customer = _botDbContext.Customers
									.Where(s => s.Id == stepContext.Context.Activity.From.Id)
									.FirstOrDefault();
				customer.Status = CustomerStatus.WatingForAgent;
				_botDbContext.Customers.Update(customer);
				await _botDbContext.SaveChangesAsync().ConfigureAwait(false);

				// Sends WebPush Notification for all Agents of this Customer Group
				IQueryable<ApplicationUser> applicationUsers = _botDbContext.ApplicationUsers
														.Where(p => p.GroupId == customer.GroupId && !string.IsNullOrEmpty(p.WebPushId));

				string msgtoshow = string.Empty;

				// Se especificou um atendente, e tem um atendente cadastrado com este nome
				string nickname = atendente.Replace("o ","").Replace("a ", "");
				if ( atendente != "um atendente" && applicationUsers.Where(p=>p.NickName == nickname).Any())
				{
					// Notifica o atendente informado
					string webPushId = applicationUsers.Where(p => p.NickName == nickname).FirstOrDefault().WebPushId;
					await _wpushApi.SendNotification("Suzi Bot", "Tem um cliente solicitando atendimento!", _configuration.GetValue<string>($"ChatUrl"), webPushId).ConfigureAwait(false);

					// Confere se foi acionado direto por intençao
					msgtoshow = $"Eu enviei uma notificação {_dialogDictionary.Emoji.LoudSpeaker} para {atendente}, que em breve vai se conectar e teclar com você. Enquanto isto, eu estou por aqui.";
				}
				else
				{
					// Verfica se indicou com qual atendente quer falar
					foreach (ApplicationUser applicationUser in applicationUsers)
					{
						// Sends WebPush Notification for this Agent
						await _wpushApi.SendNotification("Suzi Bot", "Tem um cliente solicitando atendimento!", _configuration.GetValue<string>($"ChatUrl"), applicationUser.WebPushId).ConfigureAwait(false);
					}
					// Confere se foi acionado direto por intençao
					msgtoshow = $"Eu enviei uma notificação {_dialogDictionary.Emoji.LoudSpeaker} para os atendentes. Em breve alguém vai conectar e teclar com você. Enquanto isto, eu estou por aqui.";
				}

				if (stepContext.Options != null)
				{
					// Envia a mensagem
					await stepContext.Context.SendActivityAsync(MessageFactory.Text(msgtoshow), cancellationToken).ConfigureAwait(false);
					// Envia a mensagem "algo mais?"
					await stepContext.Context.SendActivityAsync(MessageFactory.Text(_dialogDictionary.SharedMessage.MsgAlgoMais), cancellationToken).ConfigureAwait(false);
					// Finaliza o diálogo
					return await stepContext.EndDialogAsync().ConfigureAwait(false);
				}
				else
				{
					// Acrescenta "voltar ao menu" a mensagem
					msgtoshow += "\r\n" + _dialogDictionary.SharedMessage.MsgVoltarMenu;

					// Envia a mensagem como prompt de texto
					return await stepContext.PromptAsync((nameof(TextPrompt)), new PromptOptions { Prompt = MessageFactory.Text(msgtoshow) }, cancellationToken).ConfigureAwait(false);
				}

			}
			else
			{
				// Marca que esta fora de InWorkingHours
				stepContext.Values["inworkinghurs"] = "nao";

				// Avisa o cliente que estamos fora do horario de expediente
				await stepContext.Context.SendActivityAsync(MessageFactory.Text($"O horário de atendimento é de 2a a 6a feira das 09:00 as 18:00. Posso agendar para {atendente} entrar em contato a partir {NextWorkingDay()}."), cancellationToken).ConfigureAwait(false);

				// Pergunta qual o horário de sua preferência
				return await stepContext.PromptAsync(nameof(TextPrompt), new PromptOptions { Prompt = MessageFactory.Text(_dialogDictionary.Emoji.AlarmClock + " Qual é o melhor horário para você?") }, cancellationToken).ConfigureAwait(false);

			}
		}

		// Se cancelou
		//     encerra o dialogo
		// Se informou horario de preferencia
		//     envia notificação por email
		//     coloca cliente como Wating
		//     envie algo pra continuar
		private async Task<DialogTurnResult> OutOfWorkingHoursStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
		{

			string atendente = (string)stepContext.Values["atendente"];

			// Se esta fora do horario de expediente
			if ((string)stepContext.Values["inworkinghurs"] == "nao")
			{
				// Busca o que foi digitado no passo anterior
				string lasttypedinfo = ((string)stepContext.Result).ToLower();

				// Se pediu para sair
				if (lasttypedinfo == "cancelar" | lasttypedinfo == "sair" | lasttypedinfo == "voltar" | lasttypedinfo == "menu")
				{
					// Avisa o cliente que não deixou nada agendado
					await stepContext.Context.SendActivityAsync(MessageFactory.Text($"Tudo bem, não estou deixando nada agendado."), cancellationToken).ConfigureAwait(false);

					// Envia a mensagem (envia algo para continuar) como prompt de texto
					return await stepContext.PromptAsync((nameof(TextPrompt)), new PromptOptions { Prompt = MessageFactory.Text(_dialogDictionary.SharedMessage.MsgContinuar) }, cancellationToken).ConfigureAwait(false);
				}
				else
				{
					// Marca cliente com status Wating
					Customer customer = _botDbContext.Customers
										.Where(s => s.Id == stepContext.Context.Activity.From.Id)
										.FirstOrDefault();
					customer.Status = CustomerStatus.WatingForAgent;
					_botDbContext.Customers.Update(customer);
					await _botDbContext.SaveChangesAsync().ConfigureAwait(false);

					// Envia email para os atendentes notificando que tem cliente para ser atendido
					await _emailService.SendAsync(_configuration.GetValue<string>($"EmailAlert"), "Pedido de Atendimento", $"O cliente {customer.Name}, telefone {customer.MobilePhone} entrou no BOT e pediu para ser atendido por {atendente}.\r\n\r\n{lasttypedinfo}.").ConfigureAwait(false);

					// Avisa o cliente que está avisando para os atendentes entrarem em contato
					await stepContext.Context.SendActivityAsync(MessageFactory.Text($"Estarei notificando {atendente} para entrare em contato com você."), cancellationToken).ConfigureAwait(false);

					// Finaliza o diálogo
					return await stepContext.EndDialogAsync().ConfigureAwait(false);
				}

			}
			// Se está dentro, já finalizou tudo no passo anterior
			else
				// Finaliza este dialogo
				return await stepContext.EndDialogAsync().ConfigureAwait(false);

		}

		// Confere se está dentro do horário de expediente
		private static bool InWorkingHours()
		{
			// Devolve True se estiver entre segunda e sexta, das 9 as 18 - Fuso horario -3
			DateTime horabrasilia = Utility.HoraLocal();
			return (horabrasilia.DayOfWeek > DayOfWeek.Sunday & horabrasilia.DayOfWeek < DayOfWeek.Saturday & horabrasilia.Hour >= 9 & horabrasilia.Hour < 18);
		}

		// Devolve o proximo dia disponivel
		private static string NextWorkingDay()
		{
			// Devolve True se estiver entre segunda e sexta, das 9 as 18 - Fuso horario -3
			DateTime horabrasilia = Utility.HoraLocal();
			if (horabrasilia.DayOfWeek == DayOfWeek.Saturday && horabrasilia.DayOfWeek == DayOfWeek.Sunday)
				return "de segunda-feira";
			else
			{
				if (horabrasilia.Hour > 18)
					return "de amanhã";
				else
					return "das 09:00 horas";
			}
		}

	}
}

