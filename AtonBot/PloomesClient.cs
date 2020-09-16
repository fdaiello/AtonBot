
using Newtonsoft.Json;
using System;
using System.Text;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Collections.Generic;
using AdaptiveExpressions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using RestSharp;
using System.Reflection;
using System.Linq;

namespace PloomesApi
{
	// Ids dos campos extras do Contact
	public static class ContactPropertyId
    {
		public const string QuemAcompanhaVisita = "contact_B9A6BCA7-89BB-4691-8B34-E061AD7DBDE9";
		public const string Name = "contact_0DF8BF92-66C8-4B48-9A25-40081337A947";
		public const string QuemAcompanhaInstalacao = "contact_B5B5C5C5-25D6-494B-854E-E60ABE6AFA74";
	}
	// Ids dos campos extras do Deal
	public static class DealPropertyId
	{
		public const string DataVisitaTecnica = "deal_154F5521-3AAE-46B2-9491-0973850E42E4";
		public const string PeriodoVisita = "deal_BA7D7C3B-F0E6-481F-9EF5-B3B9487869EB";
		public const string HorarioVisita = "deal_6B5F432C-2438-4D2A-907C-50D6DA9C6235";
		public const string OpcaoDeInstalacao = "deal_80E104C6-6594-4783-8A90-F158ED5490C8";
		public const string EhCondominio = "deal_EFCA3F4E-1EDA-42F4-BA5C-F889E20C6010";
		public const string ResultadoValidacao = "deal_A52843BA-8989-41B8-B8DB-6401AB645D42";
		public const string TecnicoResponsavel = "deal_C1D65315-78E7-47B8-AC04-53CE12C4F7C9";
		public const string DocumentoDoTecnico = "deal_39FB2467-8E59-4B85-A786-910028568BDE";
		public const string PropostaRevisada = "deal_A7409213-F8DE-43B6-9A6E-A5B9606C045C";
		public const string PropostaAceita = "deal_D0F8DBEA-C425-4B66-BC51-9429A601DC1E";
		public const string Comprovante1aParcelaIdentificado = "deal_B37F48D5-A16D-423B-8553-872F8626E811";
		public const string DataInstalacao = "deal_C31EF5DB-21F3-453D-AEC2-09961D2D183B";
		public const string HorarioInstalacao = "deal_DB83C020-BB92-4EB6-A01B-0D80A7BD1847";
		public const string NomeTecnico1 = "deal_2657388F-8F9D-4C4C-BDE8-233EC455C604";
		public const string DocTecnico1 = "deal_07980D96-E24E-4FE8-99DD-DDA0974015E3";
		public const string NomeTecnico2 = "deal_9C1F883A-FFB8-43EE-8CF2-214682FCD10E";
		public const string DocTecnico2 = "deal_9C48007E-B4AB-47D8-918A-D90436056253";
		public const string NomeTecnico3 = "deal_34F45D74-54D7-4541-8E72-4828D7FF09A5";
		public const string DocTecnico3 = "deal_70B6D1A9-9F14-47C8-BF42-698866E6B248";
		public const string BoletoAttachmentId = "deal_37E5E69F-5708-42DE-8356-6D3BAC437A6C";
	}
	// Ids dos Periodos
	public static class PeriodoAgendamentoId
	{
		public const int Tarde = 7710080;
		public const int Manha = 7710081;
	}
	// Ids dos estágios da PipeLine
	public static class AtonStageId
	{
		public const int Nulo = 0;
		public const int Lead = 151438;
		public const int VisitaAgendada = 151439;
		public const int VisitaRealizada = 151440;
		public const int PropostaRealizada = 151441;
		public const int ValidacaoDaVisitaeProposta = 154439;
		public const int PropostaApresentada = 151442;
		public const int PropostaAceita = 152889;
		public const int InstalacaoAgendada = 152890;
	}
	public static class AtonResultadoValicacao
	{
		public const int Validada = 7992541;
	}
	// Ploomes API
	// Classe para fazer as chamadas da API Ploomes CRM - Cadastro de Leads
	public class PloomesClient
	{

		// Variaveis de configuração para usar a API
		private readonly string userKey;
		private readonly Uri serverUri;
		private readonly ILogger _logger;

		// Constructor
		public PloomesClient(IOptions<PloomesSettings> settings, ILogger<PloomesClient> logger)
		{
			if (settings == null)
				throw new ArgumentException("Argument missing:", nameof(settings));

			userKey = settings.Value.UserKey;
			serverUri = settings.Value.ServerUri;
			_logger = logger;
		}

		public async Task<int> PostContact(Contact contact)
		{
			string content = string.Empty;
			string resp = string.Empty;

			HttpClient httpClient = new HttpClient();
			try
			{
				httpClient.DefaultRequestHeaders.Add("User-Agent", "Aton-Bot");
				httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
				httpClient.DefaultRequestHeaders.Add("User-Key", userKey);
				httpClient.DefaultRequestHeaders.Add("Cache-Control", "no-cache");

				content = JsonConvert.SerializeObject(contact);

				HttpContent httpContent = new StringContent(content, Encoding.UTF8);

				httpContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

				Uri postContactUri = new Uri(serverUri.ToString() + "/Contacts");
				var httpResponseMessage = await httpClient.PostAsync(postContactUri, httpContent).ConfigureAwait(false);
				resp = await httpResponseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

				httpContent.Dispose();

				// Desserializa o objeto mensagem
				ApiContactResponse apiContactResponse = JsonConvert.DeserializeObject<ApiContactResponse>(resp);

				// Devolve o Id da mensagem
				if (apiContactResponse.Contacts != null && apiContactResponse.Contacts.Any() && apiContactResponse.Contacts[0].Id.IsNumber())
					return apiContactResponse.Contacts[0].Id;
				else
                {
					_logger.LogError("Post Contact: Error");
					_logger.LogError(resp);
					return 0;
				}

			}
			catch (Exception ex)
			{
				_logger.LogError("Post Contact: Error");
				_logger.LogError(ex.Message);
				_logger.LogError(content);
				_logger.LogError(resp);
				return 0;
			}
            finally
            {
				httpClient.Dispose();
			}

		}
		public async Task<int> PostContactByParam(string name, string phonenumber, string email, int zipcode, string city, string state, string neighborhood, string streetaddress, string streetaddressnumber, string streetaddressline2, string quemacompanha)
		{
			string content = string.Empty;
			string resp = string.Empty;

			Contact contact = new Contact { Name = name, Email = email, ZipCode = zipcode, TypeId = 2, Neighborhood = neighborhood, StreetAddress = streetaddress, StreetAddressNumber = streetaddressnumber, StreetAddressLine2 = streetaddressline2 };
			contact.AddPhone(phonenumber);
			contact.AddOtherStringProperty(ContactPropertyId.QuemAcompanhaVisita, quemacompanha);
			contact.AddOtherStringProperty(ContactPropertyId.Name, name);

			int stateId = await GetStateId(state).ConfigureAwait(false);
			if (stateId > 0)
			{
				contact.StateId = stateId;
				int cityId = await GetCityId(city.ToUpperInvariant(), stateId).ConfigureAwait(false);
				if (cityId > 0)
					contact.CityId = cityId;
			}

			HttpClient httpClient = new HttpClient();
			try
			{
				httpClient.DefaultRequestHeaders.Add("User-Agent", "Aton-Bot");
				httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
				httpClient.DefaultRequestHeaders.Add("User-Key", userKey);
				httpClient.DefaultRequestHeaders.Add("Cache-Control", "no-cache");

				content = JsonConvert.SerializeObject(contact);

				HttpContent httpContent = new StringContent(content, Encoding.UTF8);

				httpContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

				Uri postContactUri = new Uri(serverUri.ToString() + "/Contacts");
				var httpResponseMessage = await httpClient.PostAsync(postContactUri, httpContent).ConfigureAwait(false);
				resp = await httpResponseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

				httpContent.Dispose();

				// Desserializa o objeto mensagem
				ApiContactResponse apiContactResponse = JsonConvert.DeserializeObject<ApiContactResponse>(resp);

				// Devolve o Id da mensagem
				if (apiContactResponse.Contacts != null && apiContactResponse.Contacts.Any() && apiContactResponse.Contacts[0].Id.IsNumber())
					return apiContactResponse.Contacts[0].Id;
				else
				{
					_logger.LogError("Post Contact: Error");
					_logger.LogError(resp);
					return 0;
				}

			}
			catch (Exception ex)
			{
				_logger.LogError("Post Contact: Error");
				_logger.LogError(ex.Message);
				_logger.LogError(content);
				_logger.LogError(resp);
				return 0;
			}
			finally
			{
				httpClient.Dispose();
			}

		}
		public async Task<int> PatchContactByParam(int id, string name, string phonenumber, string email, int zipcode, string city, string state, string neighborhood, string streetaddress, string streetaddressnumber, string streetaddressline2, string quemacompanha)
		{
			string content = string.Empty;
			string resp = string.Empty;

			Contact contact = new Contact { Name = name, Email = email, ZipCode = zipcode, TypeId = 2, Neighborhood = neighborhood, StreetAddress = streetaddress, StreetAddressNumber = streetaddressnumber, StreetAddressLine2 = streetaddressline2 };
			contact.AddPhone(phonenumber);
			contact.AddOtherStringProperty(ContactPropertyId.QuemAcompanhaVisita, quemacompanha);
			contact.AddOtherStringProperty(ContactPropertyId.Name, name);

			int stateId = await GetStateId(state).ConfigureAwait(false);
			if (stateId > 0)
			{
				contact.StateId = stateId;
				int cityId = await GetCityId(city.ToUpperInvariant(), stateId).ConfigureAwait(false);
				if (cityId > 0)
					contact.CityId = cityId;
			}

			try
			{
				content = JsonConvert.SerializeObject(contact);

				var client = new RestClient(serverUri.ToString() + $"/Contacts({id})");
				client.Timeout = -1;
				var request = new RestRequest(Method.PATCH);
				request.AddHeader("User-Key", userKey);
				request.AddHeader("Content-Type", "application/json");
				request.AddParameter("application/json", content, ParameterType.RequestBody);
				IRestResponse response = await client.ExecuteAsync(request).ConfigureAwait(false);

				// Desserializa o objeto mensagem
				ApiContactResponse apiContactResponse = JsonConvert.DeserializeObject<ApiContactResponse>(response.Content);

				// Devolve o Id da mensagem
				if (apiContactResponse.Contacts != null && apiContactResponse.Contacts.Any() && apiContactResponse.Contacts[0].Id.IsNumber())
					return apiContactResponse.Contacts[0].Id;
				else
				{
					_logger.LogError("Patch Contact: Error");
					_logger.LogError(resp);
					return 0;
				}
			}
			catch (Exception ex)
			{
				_logger.LogError("Patch Contact: Error");
				_logger.LogError(ex.Message);
				_logger.LogError(content);
				_logger.LogError(resp);
				return 0;
			}

		}
		public async Task<int> PatchContact( Contact contact)
		{
			string content = string.Empty;
			string resp = string.Empty;

            try
			{
				content = JsonConvert.SerializeObject(contact);

				var client = new RestClient(serverUri.ToString() + $"/Contacts({contact.Id})");
				client.Timeout = -1;
				var request = new RestRequest(Method.PATCH);
				request.AddHeader("User-Key", userKey);
				request.AddHeader("Content-Type", "application/json");
				request.AddParameter("application/json", content, ParameterType.RequestBody);
				IRestResponse response = await client.ExecuteAsync(request).ConfigureAwait(false);

				// Desserializa o objeto mensagem
				ApiContactResponse apiContactResponse = JsonConvert.DeserializeObject<ApiContactResponse>(response.Content);

				// Devolve o Id da mensagem
				if (apiContactResponse.Contacts != null && apiContactResponse.Contacts.Any() && apiContactResponse.Contacts[0].Id.IsNumber())
					return apiContactResponse.Contacts[0].Id;
				else
				{
					_logger.LogError("Patch Contact: Error");
					_logger.LogError(resp);
					return 0;
				}
			}
			catch (Exception ex)
			{
				_logger.LogError("Patch Contact: Error");
				_logger.LogError(ex.Message);
				_logger.LogError(content);
				_logger.LogError(resp);
				return 0;
			}

		}
		public async Task<int> PostDeal(Deal deal)
		{
			HttpClient httpClient = new HttpClient();
			HttpContent httpContent;
			ApiContactResponse apiResponse;
			string content = string.Empty;
			string resp = string.Empty;
			try
			{
				httpClient.DefaultRequestHeaders.Add("User-Agent", "Aton-Bot");
				httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
				httpClient.DefaultRequestHeaders.Add("User-Key", userKey);
				httpClient.DefaultRequestHeaders.Add("Cache-Control", "no-cache");

				content = JsonConvert.SerializeObject(deal);

				// Retira informação de TimeZone das datas
				content = content.Replace("+00:00\"", "\"");

				httpContent = new StringContent(content, Encoding.UTF8);

				httpContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

				Uri postDealUri = new Uri(serverUri.ToString() + "/Deals");
				var httpResponseMessage = await httpClient.PostAsync(postDealUri, httpContent).ConfigureAwait(false);
				resp = await httpResponseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

				// Desserializa o objeto mensagem
				apiResponse = JsonConvert.DeserializeObject<ApiContactResponse>(resp);

				// Libera objeto
				httpContent.Dispose();

				// Devolve o Id gerado
				if (apiResponse.Contacts != null && apiResponse.Contacts.Any() && apiResponse.Contacts[0].Id.IsNumber())
					return apiResponse.Contacts[0].Id;
				else
				{
					_logger.LogError("Post Deal: Error");
					_logger.LogError(resp);
					return 0;
				}

			}
			catch (Exception ex)
			{
				_logger.LogError("PostDeal: Error");
				_logger.LogError(ex.Message);
				_logger.LogError(content);
				_logger.LogError(resp);
				httpClient.Dispose();
				return 0;
			}
            finally
            {
				httpClient.Dispose();
			}
		}
		public async Task<int> PatchDeal(Deal deal)
		{
			ApiContactResponse apiResponse;
			string content = string.Empty;
			string resp = string.Empty;
			try
			{
				// Serializa em Json o objeto deal
				content = JsonConvert.SerializeObject(deal);

				// Retira informação de TimeZone das datas
				content = content.Replace("+00:00\"", "\"");
				content = content.Replace("-03:00\"", "\"");

				// Chama a API para dar Patch no Deal
				var client = new RestClient(serverUri.ToString() + $"/Deals({deal.Id})")
				{
					Timeout = -1
				};
				var request = new RestRequest(Method.PATCH);
				request.AddHeader("User-Key", userKey);
				request.AddHeader("Content-Type", "application/json");
				request.AddParameter("application/json", content, ParameterType.RequestBody);
				IRestResponse response = await client.ExecuteAsync(request).ConfigureAwait(false);

				// Desserializa o objeto mensagem
				apiResponse = JsonConvert.DeserializeObject<ApiContactResponse>(response.Content);

				// Devolve o Id gerado
				if (apiResponse.Contacts != null && apiResponse.Contacts.Any() && apiResponse.Contacts[0].Id.IsNumber())
					return apiResponse.Contacts[0].Id;
				else
				{
					_logger.LogError("Patch Deal: Error");
					_logger.LogError(resp);
					return 0;
				}

			}
			catch (Exception ex)
			{
				_logger.LogError("Patch Deal: Error");
				_logger.LogError(ex.Message);
				_logger.LogError(content);
				_logger.LogError(resp);
				return 0;
			}
		}

		public async Task<int> GetStateId(string uf)
		{
			HttpClient httpClient = new HttpClient();
			try
			{
				httpClient.DefaultRequestHeaders.Add("User-Agent", "Aton-Bot");
				httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
				httpClient.DefaultRequestHeaders.Add("User-Key", userKey);
				httpClient.DefaultRequestHeaders.Add("Cache-Control", "no-cache");

				Uri getStateIdUri = new Uri(serverUri.ToString() + $"/Cities@Countries@States?$filter=CountryId+eq+76+and+Short+eq+'{uf}'");
				var httpResponseMessage = await httpClient.GetAsync(getStateIdUri).ConfigureAwait(false);
				string resp = await httpResponseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

				httpClient.Dispose();

				// Desserializa o objeto mensagem
				ApiContactResponse apiResponse = JsonConvert.DeserializeObject<ApiContactResponse>(resp);

				// Devolve o Id gerado
				if (apiResponse.Contacts != null && apiResponse.Contacts.Any() && apiResponse.Contacts[0].Id.IsNumber())
					return apiResponse.Contacts[0].Id;
				else
					return 0;

			}
			catch (Exception)
			{
				httpClient.Dispose();
				return 0;
			}

		}
		public async Task<int> GetCityId(string city, int stateid )
		{
			HttpClient httpClient = new HttpClient();
			try
			{
				httpClient.DefaultRequestHeaders.Add("User-Agent", "Aton-Bot");
				httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
				httpClient.DefaultRequestHeaders.Add("User-Key", userKey);
				httpClient.DefaultRequestHeaders.Add("Cache-Control", "no-cache");

				Uri getCityIdUri = new Uri(serverUri.ToString() + $"/Cities?$top=1&$filter=StateId+eq+{stateid}+and+Name+eq+'{city}'");
				var httpResponseMessage = await httpClient.GetAsync(getCityIdUri).ConfigureAwait(false);
				string resp = await httpResponseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

				httpClient.Dispose();

				// Desserializa o objeto mensagem
				ApiContactResponse apiResponse = JsonConvert.DeserializeObject<ApiContactResponse>(resp);

				// Devolve o Id gerado
				if (apiResponse.Contacts != null && apiResponse.Contacts.Any() && apiResponse.Contacts[0].Id.IsNumber())
					return apiResponse.Contacts[0].Id;
				else
					return 0;

			}
			catch (Exception)
			{
				httpClient.Dispose();
				return 0;
			}

		}

		public async Task<Contact> GetContact(int Id)
		{
			HttpClient httpClient = new HttpClient();
			string resp = string.Empty;
			Contact contact = new Contact();
			try
			{
				httpClient.DefaultRequestHeaders.Add("User-Agent", "Aton-Bot");
				httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
				httpClient.DefaultRequestHeaders.Add("User-Key", userKey);
				httpClient.DefaultRequestHeaders.Add("Cache-Control", "no-cache");

				Uri postContactUri = new Uri(serverUri.ToString() + $"/Contacts?$filter=Id+eq+{Id}");
				var httpResponseMessage = await httpClient.GetAsync(postContactUri).ConfigureAwait(false);
				resp = await httpResponseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

				// Desserializa o objeto mensagem
				ApiContactResponse apiResponse = JsonConvert.DeserializeObject<ApiContactResponse>(resp);

				// Confere se voltou conteudo
				if (apiResponse.Contacts != null && apiResponse.Contacts.Any())
					contact = apiResponse.Contacts[0];

			}
			catch (Exception ex)
			{
				_logger.LogError("Get Contact: Error");
				_logger.LogError(ex.Message);
				_logger.LogError(resp);
			}
			finally
            {
				httpClient.Dispose();
			}
			return contact;
		}

		public async Task<Deal> GetDeal(int ContactId)
		{
			HttpClient httpClient = new HttpClient();
			string resp = string.Empty;
			try
			{
				httpClient.DefaultRequestHeaders.Add("User-Agent", "Aton-Bot");
				httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
				httpClient.DefaultRequestHeaders.Add("User-Key", userKey);
				httpClient.DefaultRequestHeaders.Add("Cache-Control", "no-cache");

				Uri postContactUri = new Uri(serverUri.ToString() + $"/Deals?$filter=ContactId+eq+{ContactId}&$expand=OtherProperties");
				var httpResponseMessage = await httpClient.GetAsync(postContactUri).ConfigureAwait(false);
				resp = await httpResponseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

				httpClient.Dispose();

				// Desserializa o objeto mensagem
				ApiDealsResponse apiDealsResponse = JsonConvert.DeserializeObject<ApiDealsResponse>(resp);

				// Confere se voltou conteudo
				if (apiDealsResponse.Deals != null && apiDealsResponse.Deals.Any())
					return apiDealsResponse.Deals[0];

				else
					return null;

			}
			catch (Exception ex)
			{
				_logger.LogError("Get Deal: Error");
				_logger.LogError(ex.Message);
				_logger.LogError(resp);
				return null;
			}
			finally
			{
				httpClient.Dispose();
			}

		}
		public async Task<Quote> GetQuote(int DealId)
		{
			HttpClient httpClient = new HttpClient();
			string resp = string.Empty;
			try
			{
				httpClient.DefaultRequestHeaders.Add("User-Agent", "Aton-Bot");
				httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
				httpClient.DefaultRequestHeaders.Add("User-Key", userKey);
				httpClient.DefaultRequestHeaders.Add("Cache-Control", "no-cache");

				Uri postContactUri = new Uri(serverUri.ToString() + $"/Quotes?$filter=DealId+eq+{DealId}");
				var httpResponseMessage = await httpClient.GetAsync(postContactUri).ConfigureAwait(false);
				resp = await httpResponseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

				httpClient.Dispose();

				// Desserializa o objeto mensagem
				ApiQuoteResponse apiQuoteResponse = JsonConvert.DeserializeObject<ApiQuoteResponse>(resp);

				// Confere se voltou conteudo
				if (apiQuoteResponse.Quotes != null && apiQuoteResponse.Quotes.Any())
					return apiQuoteResponse.Quotes[^1];

				else
					return null;

			}
			catch (Exception ex)
			{
				_logger.LogError("Get Quote: Error");
				_logger.LogError(ex.Message);
				_logger.LogError(resp);
				return null;
			}
			finally
			{
				httpClient.Dispose();
			}

		}
		public async Task<PloomesAttachment> GetAttachment(long AttachmentId)
		{
			HttpClient httpClient = new HttpClient();
			string resp = string.Empty;
			try
			{
				httpClient.DefaultRequestHeaders.Add("User-Agent", "Aton-Bot");
				httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
				httpClient.DefaultRequestHeaders.Add("User-Key", userKey);
				httpClient.DefaultRequestHeaders.Add("Cache-Control", "no-cache");

				Uri postContactUri = new Uri(serverUri.ToString() + $"Attachments?$filter=Id+eq+{AttachmentId}");
				var httpResponseMessage = await httpClient.GetAsync(postContactUri).ConfigureAwait(false);
				resp = await httpResponseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

				httpClient.Dispose();

				// Desserializa o objeto mensagem
				ApiAttachmentResponse apiAttachemntResponse = JsonConvert.DeserializeObject<ApiAttachmentResponse>(resp);

				// Confere se voltou conteudo
				if (apiAttachemntResponse.Attachments != null && apiAttachemntResponse.Attachments.Any())
					return apiAttachemntResponse.Attachments[0];

				else
					return null;

			}
			catch (Exception ex)
			{
				_logger.LogError("Get Attachment: Error");
				_logger.LogError(ex.Message);
				_logger.LogError(resp);
				return null;
			}
			finally
			{
				httpClient.Dispose();
			}

		}
		static int GetOpcaodeInstalacaoCode(string opcaodeinstalacao)
		{
			// Part 1: create a List of KeyValuePairs.
			var list = new List<KeyValuePair<string, int>>();
			list.Add(new KeyValuePair<string, int>("Instalação de tomada", 8257200));
			list.Add(new KeyValuePair<string, int>("Pretendo adquirir", 8257201));
			list.Add(new KeyValuePair<string, int>("Não sei informar", 8257202));
			list.Add(new KeyValuePair<string, int>("Outros", 8257203));
			list.Add(new KeyValuePair<string, int>("Schneider", 8257204));
			list.Add(new KeyValuePair<string, int>("Efacec", 8257205));
			list.Add(new KeyValuePair<string, int>("Enel X", 8257206));

			// Part 2: loop over list and print pairs.
			foreach (var element in list)
				if (opcaodeinstalacao.ToUpperInvariant() == element.Key.ToUpperInvariant())
					return element.Value;

			return 0;
		}
	}
	public class Phone
	{
		public string PhoneNumber { get; set; }
		public int TypeId { get; set; }
		public int CountryId { get; set; }

	}

	public class Contact
	{
		public int Id { get; set; }
		public int TypeId { get; set; }
		public string Name { get; set; }
		public object LegalName { get; set; }
		public object Register { get; set; }
		public object CNPJ { get; set; }
		public object CPF { get; set; }
		public int StatusId { get; set; }
		public object CompanyId { get; set; }
		public object RelationshipId { get; set; }
		public object LineOfBusinessId { get; set; }
		public object OriginId { get; set; }
		public object NumberOfEmployeesId { get; set; }
		public int ClassId { get; set; }
		public object OwnerId { get; set; }
		public object Birthday { get; set; }
		public object NextAnniversary { get; set; }
		public object PreviousAnniversary { get; set; }
		public string Note { get; set; }
		public object Email { get; set; }
		public object Website { get; set; }
		public object RoleId { get; set; }
		public object DepartmentId { get; set; }
		public object Skype { get; set; }
		public object Facebook { get; set; }
		public object StreetAddress { get; set; }
		public string StreetAddressNumber { get; set; }
		public object StreetAddressLine2 { get; set; }
		public string Neighborhood { get; set; }
		public int ZipCode { get; set; }
		public object CityId { get; set; }
		public object StateId { get; set; }
		public object CountryId { get; set; }
		public object CurrencyId { get; set; }
		public object EmailMarketing { get; set; }
		public object CNAECode { get; set; }
		public object CNAEName { get; set; }
		public object Latitude { get; set; }
		public object Longitude { get; set; }
		public object ImportId { get; set; }
		public object CreateImportationId { get; set; }
		public object UpdateImportationId { get; set; }
		public object FirstTaskId { get; set; }
		public object FirstTaskDate { get; set; }
		public object LastInteractionRecordId { get; set; }
		public object LastDealId { get; set; }
		public object LastOrderId { get; set; }
		public int TasksOrdination { get; set; }
		public object LeadId { get; set; }
		public bool Editable { get; set; }
		public bool Deletable { get; set; }
		public int CreatorId { get; set; }
		public object UpdaterId { get; set; }
		public DateTime CreateDate { get; set; }
		public DateTime LastUpdateDate { get; set; }
		public object Key { get; set; }
		public object LastDocumentId { get; set; }
		public object AvatarUrl { get; set; }
		public List<Phone> Phones { get; } = new List<Phone>();
		internal void AddPhone(string phonenumber, int typeid = 0, int coutryid = 0)
		{
			if ( !this.Phones.Where(p=>p.PhoneNumber == phonenumber & p.CountryId==coutryid).Any())
				this.Phones.Add(new Phone { PhoneNumber = phonenumber, TypeId = typeid, CountryId = coutryid });
		}
		public List<OtherProperty> OtherProperties { get; } = new List<OtherProperty>();

		internal void AddOtherStringProperty(string fieldkey, string stringvalue)
		{
			this.OtherProperties.Add(new OtherProperty { FieldKey = fieldkey, StringValue = stringvalue });
		}
		internal void AddOtherIntegerProperty(string fieldkey, int integerValue)
		{
			this.OtherProperties.Add(new OtherProperty { FieldKey = fieldkey, IntegerValue = integerValue });
		}
		internal void AddOtherDateTimeProperty(string fieldkey, object datetimeValue)
		{
			this.OtherProperties.Add(new OtherProperty { FieldKey = fieldkey, DateTimeValue = datetimeValue });
		}
		internal void AddOtherBoolProperty(string fieldkey, bool boolValue)
		{
			this.OtherProperties.Add(new OtherProperty { FieldKey = fieldkey, BoolValue = boolValue });
		}
		internal void AddOtherProperty(string fieldkey, string stringvalue, object  integervalue, object datetimevalue, object boolvalue)
		{
			this.OtherProperties.Add(new OtherProperty { FieldKey = fieldkey, StringValue = stringvalue, IntegerValue = integervalue, DateTimeValue = datetimevalue, BoolValue = boolvalue });
		}
		public void MarcaQuemAcompanhaVisita(string quemAcompanhaVisita)
		{
			OtherProperty otherProperty = this.OtherProperties.Where(p => p.FieldKey == ContactPropertyId.QuemAcompanhaVisita).FirstOrDefault();
			if (otherProperty == null)
				this.AddOtherStringProperty(ContactPropertyId.QuemAcompanhaVisita, quemAcompanhaVisita);
			else
				otherProperty.StringValue = quemAcompanhaVisita;
		}
		public void MarcaQuemAcompanhaInstalacao(string quemAcompanhaInstalacao)
		{
			OtherProperty otherProperty = this.OtherProperties.Where(p => p.FieldKey == ContactPropertyId.QuemAcompanhaInstalacao).FirstOrDefault();
			if (otherProperty == null)
				this.AddOtherStringProperty(ContactPropertyId.QuemAcompanhaInstalacao, quemAcompanhaInstalacao);
			else
				otherProperty.StringValue = quemAcompanhaInstalacao;
		}
		public void MarcaName(string name)
		{
			OtherProperty otherProperty = this.OtherProperties.Where(p => p.FieldKey == ContactPropertyId.Name).FirstOrDefault();
			if (otherProperty == null)
				this.AddOtherStringProperty(ContactPropertyId.Name, name);
			else
				otherProperty.StringValue = name;
		}
		public void CopyFrom(Contact contact)
		{
			if ( contact != null)
            {
				foreach (PropertyInfo property in typeof(Contact).GetProperties().Where(p => p.CanWrite))
				{
					property.SetValue(this, property.GetValue(contact, null), null);
				}
				foreach (OtherProperty otherProperty in contact.OtherProperties)
				{
					this.AddOtherProperty(otherProperty.FieldKey, otherProperty.StringValue, otherProperty.IntegerValue, otherProperty.DateTimeValue, otherProperty.BoolValue);
				}
			}
		}
	}

	public class ApiContactResponse
	{
		[JsonProperty("@odata.context")]
		public string Odata { get; set; }
		[JsonProperty("value")]
		public List<Contact> Contacts { get; } = new List<Contact>();

	}
	public class OtherProperty
	{
		public string FieldKey { get; set; }
		public string StringValue { get; set; }
		public object DateTimeValue { get; set; }
		public object IntegerValue { get; set; }
		public object BoolValue { get; set; }
	}
	public class ApiDealsResponse
	{
		[JsonProperty("@odata.context")]
		public string Odata { get; set; }
		[JsonProperty("value")]
		public List<Deal> Deals { get; } = new List<Deal>();
    }

	public class Deal
	{
		public int Id { get; set; }
		public string Title { get; set; }
		public int ContactId { get; set; }
		public string ContactName { get; set; }
		public object PersonId { get; set; }
		public object PersonName { get; set; }
		public int PipelineId { get; set; }
		public int StageId { get; set; }
		public int StatusId { get; set; }
		public object FirstTaskId { get; set; }
		public object FirstTaskDate { get; set; }
		public bool HasScheduledTasks { get; set; }
		public int TasksOrdination { get; set; }
		public object ContactProductId { get; set; }
		public object LastQuoteId { get; set; }
		public bool IsLastQuoteApproved { get; set; }
		public object WonQuoteId { get; set; }
		public object LastStageId { get; set; }
		public object LossReasonId { get; set; }
		public object OriginId { get; set; }
		public object OwnerId { get; set; }
		public object FinishDate { get; set; }
		public int CurrencyId { get; set; }
		public decimal Amount { get; set; }
		public int StartCurrencyId { get; set; }
		public decimal StartAmount { get; set; }
		public bool Read { get; set; }
		public object LastInteractionRecordId { get; set; }
		public object LastOrderId { get; set; }
		public int DaysInStage { get; set; }
		public int HoursInStage { get; set; }
		public int Length { get; set; }
		public object CreateImportId { get; set; }
		public object UpdateImportId { get; set; }
		public object LeadId { get; set; }
		public object OriginDealId { get; set; }
		public object ReevId { get; set; }
		public bool Editable { get; set; }
		public bool Deletable { get; set; }
		public int CreatorId { get; set; }
		public int UpdaterId { get; set; }
		public DateTime CreateDate { get; set; }
		public DateTime LastUpdateDate { get; set; }
		public object LastDocumentId { get; set; }
		public object DealNumber { get; set; }
		public List<OtherProperty> OtherProperties { get; } = new List<OtherProperty>();
		public List<Quote> Quotes { get; } = new List<Quote>();
		internal void AddOtherStringProperty(string fieldkey, string stringvalue)
		{
			this.OtherProperties.Add(new OtherProperty { FieldKey = fieldkey, StringValue = stringvalue });
		}
		internal void AddOtherIntegerProperty(string fieldkey, int integerValue)
		{
			this.OtherProperties.Add(new OtherProperty { FieldKey = fieldkey, IntegerValue = integerValue });
		}
		internal void AddOtherDateTimeProperty(string fieldkey, object datetimeValue)
		{
			this.OtherProperties.Add(new OtherProperty { FieldKey = fieldkey, DateTimeValue = datetimeValue });
		}
		internal void AddOtherBoolProperty(string fieldkey, bool boolValue)
		{
			this.OtherProperties.Add(new OtherProperty { FieldKey = fieldkey, BoolValue = boolValue });
		}
        internal void AddOtherProperty(string fieldkey, string stringvalue, object integervalue, object datetimevalue, object boolvalue)
        {
			this.OtherProperties.Add(new OtherProperty { FieldKey = fieldkey, StringValue = stringvalue, IntegerValue = integervalue, DateTimeValue = datetimevalue, BoolValue = boolvalue }); 
        }
		public void CopyFrom(Deal deal)
		{
			if ( deal != null)
            {
				foreach (PropertyInfo property in typeof(Deal).GetProperties().Where(p => p.CanWrite))
				{
					property.SetValue(this, property.GetValue(deal, null), null);
				}
				foreach (OtherProperty otherProperty in deal.OtherProperties)
				{
					this.AddOtherProperty(otherProperty.FieldKey, otherProperty.StringValue, otherProperty.IntegerValue, otherProperty.DateTimeValue, otherProperty.BoolValue);
				}
			}
		}
		public void MarcaPropostaAceita(bool aceita)
        {
			OtherProperty otherProperty = this.OtherProperties.Where(p => p.FieldKey == DealPropertyId.PropostaAceita).FirstOrDefault();
			if (otherProperty == null)
				this.AddOtherBoolProperty(DealPropertyId.PropostaAceita, aceita);
			else
				otherProperty.BoolValue = aceita;
		}
		public void MarcaDataVisitaTecnica ( DateTime dataVisita)
        {
			OtherProperty otherProperty = this.OtherProperties.Where(p => p.FieldKey == DealPropertyId.DataVisitaTecnica).FirstOrDefault();
			if (otherProperty == null)
				this.AddOtherDateTimeProperty(DealPropertyId.DataVisitaTecnica, dataVisita);
			else
				otherProperty.DateTimeValue = dataVisita;
		}
		public void MarcaPeriodoVisitaTecnica(string periodo)
		{
			int periodoId;
			if (periodo == "tarde")
				periodoId = PeriodoAgendamentoId.Tarde;
			else
				periodoId = PeriodoAgendamentoId.Manha;

			OtherProperty otherProperty = this.OtherProperties.Where(p => p.FieldKey == DealPropertyId.PeriodoVisita).FirstOrDefault();
			if (otherProperty == null)
				this.AddOtherIntegerProperty(DealPropertyId.PeriodoVisita, periodoId);
			else
				otherProperty.IntegerValue = periodoId;
		}
		public void MarcaHorarioVisitaTecnica(DateTime horarioVisita)
		{
			OtherProperty otherProperty = this.OtherProperties.Where(p => p.FieldKey == DealPropertyId.HorarioVisita).FirstOrDefault();
			if (otherProperty == null)
				this.AddOtherDateTimeProperty(DealPropertyId.HorarioVisita, horarioVisita);
			else
				otherProperty.DateTimeValue = horarioVisita;
		}
		public void MarcaOpcaoInstalacao(string opcaodeinstalacao)
		{
			int opcaodeinstalacaoCode = GetOpcaodeInstalacaoCode(opcaodeinstalacao);

			OtherProperty otherProperty = this.OtherProperties.Where(p => p.FieldKey == DealPropertyId.OpcaoDeInstalacao).FirstOrDefault();
			if (otherProperty == null)
				this.AddOtherIntegerProperty(DealPropertyId.OpcaoDeInstalacao, opcaodeinstalacaoCode);
			else
				otherProperty.IntegerValue = opcaodeinstalacaoCode;
		}
		public void MarcaEhCondominio(bool ehCondominio)
		{
			OtherProperty otherProperty = this.OtherProperties.Where(p => p.FieldKey == DealPropertyId.EhCondominio).FirstOrDefault();
			if (otherProperty == null)
				this.AddOtherBoolProperty(DealPropertyId.EhCondominio, ehCondominio);
			else
				otherProperty.BoolValue = ehCondominio;
		}
		public void MarcaDataInstalacao(DateTime dataInstalacao)
		{
			OtherProperty otherProperty = this.OtherProperties.Where(p => p.FieldKey == DealPropertyId.DataInstalacao).FirstOrDefault();
			if (otherProperty == null)
				this.AddOtherDateTimeProperty(DealPropertyId.DataInstalacao, dataInstalacao);
			else
				otherProperty.DateTimeValue = dataInstalacao;
		}
		public void MarcaHorarioInstalacao(DateTime horarioInstalacao)
		{
			OtherProperty otherProperty = this.OtherProperties.Where(p => p.FieldKey == DealPropertyId.HorarioInstalacao).FirstOrDefault();
			if (otherProperty == null)
				this.AddOtherDateTimeProperty(DealPropertyId.HorarioInstalacao, horarioInstalacao);
			else
				otherProperty.DateTimeValue = horarioInstalacao;
		}

		static int GetOpcaodeInstalacaoCode(string opcaodeinstalacao)
		{
			// Part 1: create a List of KeyValuePairs.
			var list = new List<KeyValuePair<string, int>>();
			list.Add(new KeyValuePair<string, int>("Instalação de tomada", 8257200));
			list.Add(new KeyValuePair<string, int>("Pretendo adquirir", 8257201));
			list.Add(new KeyValuePair<string, int>("Não sei informar", 8257202));
			list.Add(new KeyValuePair<string, int>("Outros", 8257203));
			list.Add(new KeyValuePair<string, int>("Schneider", 8257204));
			list.Add(new KeyValuePair<string, int>("Efacec", 8257205));
			list.Add(new KeyValuePair<string, int>("Enel X", 8257206));

			// Part 2: loop over list and print pairs.
			foreach (var element in list)
				if (opcaodeinstalacao.ToUpperInvariant() == element.Key.ToUpperInvariant())
					return element.Value;

			return 0;
		}
	}
	public class Quote
	{
		public int Id { get; set; }
		public int ContactId { get; set; }
		public string ContactName { get; set; }
		public int DealId { get; set; }
		public int OwnerId { get; set; }
		public int TemplateId { get; set; }
		public bool IsTemplate { get; set; }
		public DateTime Date { get; set; }
		public int QuoteNumber { get; set; }
		public int ReviewNumber { get; set; }
		public bool LastReview { get; set; }
		public int LastReviewId { get; set; }
		public bool Approver { get; set; }
		public object ApprovalStatusId { get; set; }
		public object ApprovalLevelId { get; set; }
		public object PersonId { get; set; }
		public object PersonName { get; set; }
		public int CurrencyId { get; set; }
		public double Amount { get; set; }
		public double Discount { get; set; }
		public object ExpirationDate { get; set; }
		public object InstallmentsNumber { get; set; }
		public object DeliveryTime { get; set; }
		public object PaymentMethod { get; set; }
		public object Title { get; set; }
		public object Description { get; set; }
		public object Notes { get; set; }
		public object FreightModal { get; set; }
		public object FreightCost { get; set; }
		public string Key { get; set; }
		public bool Shared { get; set; }
		public object EmailSenderTypeId { get; set; }
		public object EmailSenderUserId { get; set; }
		public object ExternallyAccepted { get; set; }
		public bool ExternalNotifications { get; set; }
		public object ExternalSharingDate { get; set; }
		public object HeaderSourceCode { get; set; }
		public object HeaderHeight { get; set; }
		public object FooterSourceCode { get; set; }
		public object FooterHeight { get; set; }
		public object BodySourceCode { get; set; }
		public object PreviewSourceCode { get; set; }
		public int TopMargin { get; set; }
		public int BottomMargin { get; set; }
		public int SideMargin { get; set; }
		public bool HasCoverPage { get; set; }
		public object CoverSourceCode { get; set; }
		public bool HasPaging { get; set; }
		public object FileName { get; set; }
		public object EmailId { get; set; }
		public string DocumentUrl { get; set; }
		public int CreatorId { get; set; }
		public object UpdaterId { get; set; }
		public DateTime CreateDate { get; set; }
		public DateTime LastUpdateDate { get; set; }
		public object LastExternalOpeningDate { get; set; }
		public bool Editable { get; set; }
	}
	public class ApiQuoteResponse
	{
		[JsonProperty("@odata.context")]
		public string Odata { get; set; }
		[JsonProperty("value")]
		public List<Quote> Quotes { get; } = new List<Quote>();
	}
	public class PloomesAttachment
	{
		public int Id { get; set; }
		public object ContactId { get; set; }
		public int DealId { get; set; }
		public object OrderId { get; set; }
		public object NoteId { get; set; }
		public object InteractionRecordId { get; set; }
		public object EmailId { get; set; }
		public object EmailTemplateId { get; set; }
		public object LeadId { get; set; }
		public object TaskId { get; set; }
		public object DocumentId { get; set; }
		public object QuoteId { get; set; }
		public object ProductId { get; set; }
		public object ProductGroupId { get; set; }
		public object ProductFamilyId { get; set; }
		public object AccountId { get; set; }
		public object UserId { get; set; }
		public object ChatId { get; set; }
		public object MessageId { get; set; }
		public string FileName { get; set; }
		public string ContentType { get; set; }
		public int ContentLength { get; set; }
		public string Url { get; set; }
		public bool Listable { get; set; }
	}
	public class ApiAttachmentResponse
	{
		[JsonProperty("@odata.context")]
		public string Odata { get; set; }
		[JsonProperty("value")]
		public List<PloomesAttachment> Attachments { get; } = new List<PloomesAttachment>();
	}
#pragma warning disable CA1812
	internal class State
	{
		public int Id { get; set; }
		public string Short { get; set; }
		public string Name { get; set; }
		public int CountryId { get; set; }

	}
	internal class City
	{
		public int Id { get; set; }
		public string Name { get; set; }
		public int CountryId { get; set; }
		public int StateId { get; set; }
		public int IBGECode { get; set; }
		public DateTime LastUpdateDate { get; set; }
		public bool Editable { get; set; }

	}
#pragma warning restore CA1812

}
