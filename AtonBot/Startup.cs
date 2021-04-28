// Mr Bot 2020
// Mister Postman
// Felipe Daiello
// fdaiello@misterpostman.com.br

using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.ApplicationInsights;
using Microsoft.Bot.Builder.Azure;
using Microsoft.Bot.Builder.Integration.ApplicationInsights.Core;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Builder.LanguageGeneration;
using Microsoft.Bot.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MrBot.Bots;
using ContactCenter.Data;
using MrBot.Dialogs;
using MisterBot.Infrastructure.Middleware;
using MrBot.CognitiveModels;
using NETCore.MailKit.Extensions;
using NETCore.MailKit.Infrastructure.Internal;
using System;
using System.Collections.Concurrent;
using System.IO;
using Azure.Storage.Blobs;
using PloomesApi;
using MisterBot.Infrastructure.Adaters;
using MisterBot.Infrastructure.Adaters.GsWhatsApp;
using ContactCenter.Infrastructure.Clients.GsWhatsApp;
using ContactCenter.Infrastructure.Clients.Wpush;
using ContactCenter.Infrastructure.Clients.Speech;
using ContactCenter.Core.Models;

namespace MrBot
{
	public class Startup
	{
		// Configuration
		public IConfiguration Configuration { get; }

		// Logger
		private readonly ILogger _logger;

		public Startup(IWebHostEnvironment env, ILogger<Startup> logger)
		{

			var builder = new ConfigurationBuilder()
				.SetBasePath(env.ContentRootPath)
				.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
				.AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true)
				.AddEnvironmentVariables();

			Configuration = builder.Build();

			_logger = logger;
		}

		// This method gets called by the runtime. Use this method to add services to the container.
		public void ConfigureServices(IServiceCollection services)
		{

			// Logger
			_logger.LogInformation("Aton-Bot Start up");

			// Configure MVC
			services.AddControllers();

			// Create the Bot Framework Adapter with error handling enabled.
			services.AddSingleton<IBotFrameworkHttpAdapter, AdapterWithTranscriptAndErrorHandler>();

			// Create the GupShup WhatsApp Adapter with error handling enabled.
			services.AddSingleton<GsWhatsAppAdapter, GsWhatsAppAdapterWithErrorHandler>();

			//// Esta opção serve para trocar o banco para um SQL Server
			services.AddDbContext<ApplicationDbContext>(options =>
					options.UseSqlServer(Configuration.GetConnectionString("DefaultConnection"),
					opts => opts.CommandTimeout((int)TimeSpan.FromSeconds(10).TotalSeconds)));

			// Create the storage client we'll be using for User and Conversation state.
			var statestorage = new AzureBlobStorage(Configuration.GetValue<string>($"BlobStorageConnStr"), Configuration.GetValue<string>($"BotStateContainer"));

			// Conversation state - usado pelo sistema de Dialogos
			var conversationState = new ConversationState(statestorage);
			services.AddSingleton(conversationState);

			// Create the storage client used to upload media files
			BlobContainerClient blobContainerClient = new BlobContainerClient(Configuration.GetValue<string>($"BlobStorageConnStr"), Configuration.GetValue<string>($"FileContainer"));
			services.AddSingleton(blobContainerClient);

			// Create a global hashset for our ConversationReferences
			services.AddSingleton<ConcurrentDictionary<string, ConversationReference>>();

			// Registra o Middleware que faz o log dos Chats - passa as configurações do MbContext
			var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
			optionsBuilder.UseSqlServer(Configuration.GetConnectionString("DefaultConnection"),
					opts => opts.CommandTimeout((int)TimeSpan.FromSeconds(20).TotalSeconds));

			var transcriptMiddleware = new TranscriptLoggerMiddleware(new DbContextTranscriptStore(optionsBuilder, _logger));
			services.AddSingleton(transcriptMiddleware);

			//Add MailKit
			services.AddMailKit(optionBuilder =>
			{
				optionBuilder.UseMailKit(new MailKitOptions()
				{
					//get options from sercets.json
					Server = Configuration["MailKit:Server"],
					Port = Convert.ToInt32(Configuration["MailKit:Port"]),
					SenderName = Configuration["MailKit:SenderName"],
					SenderEmail = Configuration["MailKit:SenderEmail"],

					// can be optional with no authentication 
					Account = Configuration["MailKit:Account"],
					Password = Configuration["MailKit:Password"],

					// enable ssl or tls
					Security = true
				});
			});

			// WebPush Client
			IConfigurationSection wpushsettings = Configuration.GetSection("WebPushR");
			services.Configure<WpushSettings>(wpushsettings);
			services.AddSingleton<WpushClient>();

			// Speech Client
			IConfigurationSection speechSettings = Configuration.GetSection("Speech");
			services.Configure<SpeechSettings>(speechSettings);
			services.AddSingleton<SpeechClient>();

			// Ploomes Api
			IConfigurationSection ploomessettings = Configuration.GetSection("PloomesApi");
			services.Configure<PloomesSettings>(ploomessettings);
			services.AddSingleton<PloomesClient>();

			// Dicionario com smileys e msgs para os diálogos
			services.AddSingleton<DialogDictionary>();

			// Cria os dialogos que o Bot vai precisar
			services.AddTransient<RootDialog>();
			services.AddTransient<ProfileDialog>();
			services.AddTransient<ProfileDialog2>();
			services.AddTransient<CallHumanDialog>();
			services.AddTransient<QnAMakerConfiguration>();
			services.AddTransient<QnAMakerMultiturnDialog>();
			services.AddTransient<QuerAtendimentoDialog>();
			services.AddTransient<MainMenuDialog>();
			services.AddTransient<AgendaVisitaDialog>();
			services.AddTransient<ReAgendaVisitaDialog>();
			services.AddTransient<EnviaPropostaDialog>();
			services.AddTransient<AgendaInstalacaoDialog>();
			services.AddTransient<AskDateDialog>();


			// Langage Generation
			Templates lgTemplates = Templates.ParseFile(Path.Combine(".", "Responses", $"MainResponses.{Configuration.GetValue<string>($"DefaultLocale")}.lg"));
			services.AddSingleton(lgTemplates);

			// Configure telemetry
			services.AddApplicationInsightsTelemetry();
			services.AddSingleton<IBotTelemetryClient, BotTelemetryClient>();
			services.AddSingleton<ITelemetryInitializer, OperationCorrelationTelemetryInitializer>();
			services.AddSingleton<ITelemetryInitializer, TelemetryBotIdInitializer>();
			services.AddSingleton<TelemetryInitializerMiddleware>();

			// Add the telemetry initializer middleware com opção para registrar informações pessoais
			services.AddSingleton<TelemetryLoggerMiddleware>(sp =>
			{
				var telemetryClient = sp.GetService<IBotTelemetryClient>();
				return new TelemetryLoggerMiddleware(telemetryClient, logPersonalInformation: true);
			});

			// Create the bot services configuration (QnA) as a singleton.
			services.AddSingleton<IQnAMakerConfiguration, QnAMakerConfiguration>();

			// Register LUIS recognizer
			services.AddSingleton<MisterBotRecognizer>();

			// Create the bot as a transient. In this case the ASP Controller is expecting an IBot.
			services.AddTransient<IBot, AtonBot>();

			// Cria um Customer pra compartilhar entre os diálogos
			services.AddScoped<Contact>();

			// Cria um Ploomes Contact para compartilhar entre os diálogos
			services.AddScoped<PloomesApi.PloomesContact>();

			// Cria um Ploomes Deal para compartilhar entre os diálogos
			services.AddScoped<Deal>();

			// Get Gs ( GupShupAPI ) Settings & API
			IConfigurationSection gsSettings = Configuration.GetSection("GsSettings");
			services.Configure<GsWhatsAppSettings>(gsSettings);
			services.AddSingleton<GsWhatsAppClient>();
		}

		// This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
		public static void Configure(IApplicationBuilder app, IWebHostEnvironment env)
		{

			if (env.IsDevelopment())
			{
				app.UseDeveloperExceptionPage();
			}

			app.UseDefaultFiles()
				.UseStaticFiles()
				.UseWebSockets()
				.UseRouting()
				.UseEndpoints(endpoints => endpoints.MapControllers());

		}
	}
}
