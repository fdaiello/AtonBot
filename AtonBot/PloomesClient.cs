
using Newtonsoft.Json;
using System;
using System.Text;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Collections.Generic;
using AdaptiveExpressions;
using Microsoft.Extensions.Options;

namespace PloomesApi
{
	// Ploomes API
	// Classe para fazer as chamadas da API Ploomes CRM - Cadastro de Leads
	public class PloomesClient
	{

		// Variaveis de configuração para usar a API
		private readonly string userKey;
		private readonly Uri serverUri;

		// Constructor
		public PloomesClient(IOptions<PloomesSettings> settings)
		{
			if (settings == null)
				throw new ArgumentException("Argument missing:", nameof(settings));

			userKey = settings.Value.UserKey;
			serverUri = settings.Value.ServerUri;
		}

		public async Task<int> PostContact(string name, string phonenumber, int zipcode, string city, string state, string neighborhood, string streetaddress, string streetaddressnumber, string streetaddressline2)
		{

			Contact contact = new Contact { Name = name, ZipCode = zipcode, TypeId=2, Neighborhood = neighborhood, StreetAddress = streetaddress, StreetAddressNumber = streetaddressnumber, StreetAddressLine2 = streetaddressline2  };
			contact.AddPhone(phonenumber);

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

				string content = JsonConvert.SerializeObject(contact);

				HttpContent httpContent = new StringContent(content, Encoding.UTF8);

				httpContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

				Uri postContactUri = new Uri(serverUri.ToString() + "/Contacts");
				var httpResponseMessage = await httpClient.PostAsync(postContactUri, httpContent).ConfigureAwait(false);
				string resp = await httpResponseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

				httpContent.Dispose();
				httpClient.Dispose();

				// Desserializa o objeto mensagem
				ApiResponse apiResponse = JsonConvert.DeserializeObject<ApiResponse>(resp);

				// Devolve o Id da mensagem
				if (apiResponse.Value != null && apiResponse.Value.Count>0 && apiResponse.Value[0].Id.IsNumber())
					return apiResponse.Value[0].Id;
				else
					return 0;

			}
			catch (Exception)
			{
				httpClient.Dispose();
				return 0;
			}

		}
		public async Task<int> PostDeal(int contactid, string title, DateTime data, string periodo, DateTime horario )
		{

			Deal deal = new Deal { Title = title, ContactId = contactid };
			deal.AddOtherProperty("deal_154F5521-3AAE-46B2-9491-0973850E42E4",null,data,null);									          // Data: DateTimeValue, format yyyy-MM-dd
			if ( periodo == "tarde")
				deal.AddOtherProperty("deal_BA7D7C3B-F0E6-481F-9EF5-B3B9487869EB", null, null, 7710080);								  // Turno: "TableId": 18233 -> 7710080=tarde, 7710081=manha
			else
				deal.AddOtherProperty("deal_BA7D7C3B-F0E6-481F-9EF5-B3B9487869EB", null, null, 7710081);
			deal.AddOtherProperty("deal_6B5F432C-2438-4D2A-907C-50D6DA9C6235", null, horario, null);		  // Horario: DateTimeValue, format yyyy-MM-ddTHH:mm

			HttpClient httpClient = new HttpClient();
			try
			{
				httpClient.DefaultRequestHeaders.Add("User-Agent", "Aton-Bot");
				httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
				httpClient.DefaultRequestHeaders.Add("User-Key", userKey);
				httpClient.DefaultRequestHeaders.Add("Cache-Control", "no-cache");

				string content = JsonConvert.SerializeObject(deal);

				HttpContent httpContent = new StringContent(content, Encoding.UTF8);

				httpContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

				Uri postDealUri = new Uri(serverUri.ToString() + "/Deals");
				var httpResponseMessage = await httpClient.PostAsync(postDealUri, httpContent).ConfigureAwait(false);
				string resp = await httpResponseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

				httpContent.Dispose();
				httpClient.Dispose();

				// Desserializa o objeto mensagem
				ApiResponse apiResponse = JsonConvert.DeserializeObject<ApiResponse>(resp);

				// Devolve o Id gerado
				if (apiResponse.Value != null && apiResponse.Value.Count > 0 && apiResponse.Value[0].Id.IsNumber())
					return apiResponse.Value[0].Id;
				else
					return 0;

			}
			catch (Exception)
			{
				httpClient.Dispose();
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
				ApiResponse apiResponse = JsonConvert.DeserializeObject<ApiResponse>(resp);

				// Devolve o Id gerado
				if (apiResponse.Value != null && apiResponse.Value.Count > 0 && apiResponse.Value[0].Id.IsNumber())
					return apiResponse.Value[0].Id;
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
				ApiResponse apiResponse = JsonConvert.DeserializeObject<ApiResponse>(resp);

				// Devolve o Id gerado
				if (apiResponse.Value != null && apiResponse.Value.Count > 0 && apiResponse.Value[0].Id.IsNumber())
					return apiResponse.Value[0].Id;
				else
					return 0;

			}
			catch (Exception)
			{
				httpClient.Dispose();
				return 0;
			}

		}
	}
	internal class Phone
	{
		public string PhoneNumber { get; set; }
		public int TypeId { get; set; }
		public int CountryId { get; set; }

	}

	internal class Contact
	{
		public string Name { get; set; }
		public string Neighborhood { get; set; }
		public int ZipCode { get; set; }
		public int OriginId { get; set; }
		public object CompanyId { get; set; }
		public string StreetAddress { get; set; }
		public string StreetAddressNumber { get; set; }
		public string StreetAddressLine2 { get; set; }
		public int CityId { get; set; }
		public int StateId { get; set; }
		public int CountryId { get; set; }
		public int TypeId { get; set; }
		public string Note { get; set; }
		public List<Phone> Phones { get; } = new List<Phone>();

		internal void AddPhone( string phonenumber, int typeid=0, int coutryid = 0)
		{
			this.Phones.Add(new Phone { PhoneNumber = phonenumber, TypeId = typeid, CountryId = coutryid });
		}

	}

#pragma warning disable CA1812
	internal class Value
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

	}
	internal class ApiResponse
	{
		[JsonProperty("@odata.context")]
		public string Odata { get; set; }
		[JsonProperty("value")]
		public List<Value> Value { get; set; }

	}
	internal class OtherProperty
	{
		public string FieldKey { get; set; }
		public object StringValue { get; set; }
		public object DateTimeValue { get; set; }
		public object IntegerValue { get; set; }

	}

	internal class Deal
	{
		public string Title { get; set; }
		public int ContactId { get; set; }
		public List<OtherProperty> OtherProperties { get; } = new List<OtherProperty>();
		internal void AddOtherProperty(string fieldkey, object stringvalue, object datetimevalue, object integervalue)
		{
			this.OtherProperties.Add(new OtherProperty { FieldKey = fieldkey, StringValue = stringvalue, DateTimeValue = datetimevalue, IntegerValue = integervalue });
		}
	}
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
