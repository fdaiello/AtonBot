using Azure.Storage.Blobs;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.AI.Luis;
using Microsoft.Bot.Schema;
using Microsoft.Bot.Builder.Dialogs;
using MrBot.CognitiveModels;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace MrBot
{
	// Utility Methods
	public static class Utility
	{
		// Confere se a mensagem tem algum arquivo anexado. Se tiver, faz o download, e devolve o nome do arquivo
		public static async Task<string> SaveAttachmentAsync(ITurnContext turnContext, BlobContainerClient blobContainerClient)
		{

			// Media FileName
			string filename = string.Empty;

			// Se veio algum arquivo anexado
			if (turnContext.Activity.Attachments != null && turnContext.Activity.Attachments.Any())
			{
				// Search all file objects within the incoming Activity
				foreach (var file in turnContext.Activity.Attachments)
				{

					// Nome unico para o arquivo
					filename = UniqueFileName(turnContext.Activity, file.Name);

					// Referencia para o arquivo no container
					BlobClient blob = blobContainerClient.GetBlobClient(filename);

					// Determine where the file is hosted.
					var contentUrl = file.ContentUrl;

					// Stream para ler o conteudo do arquivo
					Stream stream;

					// Se é attachment Inline: data dentro da URL no formato "data:audio/ogg;base64,iVBORw0KGgo…"
					if (contentUrl.StartsWith("data:"))
					{
						// cria stream com o conteudo - primeiro passa pra array de bytes
						string data = contentUrl.Split("base64,").Last();
						byte[] bytes = Convert.FromBase64String(data);
						stream = new MemoryStream(bytes);
					}

					// Se é URL hospedada na Web
					else
					{
						// Download the actual attachment
						using var webClient = new WebClient();
						Uri uri = new Uri(contentUrl);
						stream = new MemoryStream(webClient.DownloadData(uri));
					}

					// Faz upload da stream para o Blob
					await blob.DeleteIfExistsAsync().ConfigureAwait(false);
					await blob.UploadAsync(stream).ConfigureAwait(false);

					// Libera a stream
					stream.Dispose();

				}

			}

			return filename;
		}
		public static string UniqueFileName(IActivity activity, string filename)
		{

			if ( activity.Timestamp == null)
			{
				return activity.Id + "-" + RemoveAcentos(filename).Replace(" ", "-");
			}
			else
			{
				DateTimeOffset dto = activity.Timestamp ?? DateTime.UtcNow;
				return dto.ToString("o", new CultureInfo("en-US")).Replace(":", "-").Replace(".", "-") + "-" + RemoveAcentos(filename).Replace(" ", "-");
			}


		}
		// Confere se a mensagem tem algum arquivo anexado. Se tiver, faz o download, e devolve o nome do arquivo
		public static string SaveAttachmentLocal(ITurnContext turnContext)
		{
			// Media UploadPath
			string MediaUploadsPath = Path.Combine(Environment.CurrentDirectory, @"wwwroot\MediaUploads");
			// Media FileName
			string filename = string.Empty;

			// Se o diretorio wwwroot\MediaUploads nao exite, cria
			if (!Directory.Exists(MediaUploadsPath))
				Directory.CreateDirectory(MediaUploadsPath);

			// Se veio algum arquivo anexado
			if (turnContext.Activity.Attachments != null && turnContext.Activity.Attachments.Any())
			{
				// Search all file objects within the incoming Activity
				foreach (var file in turnContext.Activity.Attachments)
				{
					// Determine where the file is hosted.
					var contentUrl = file.ContentUrl;

					// Sets filename 
					var localFileName = Path.Combine(MediaUploadsPath, file.Name);

					// Consulta se é hospedado na Web ou se é Inline: data dentro da URL no formato "data:audio/ogg;base64,iVBORw0KGgo…"
					if (contentUrl.StartsWith("data:"))
					{
						if (!File.Exists(localFileName))
						{
							// cria stream com o conteudo - primeiro passa pra array de bytes
							string data = contentUrl.Split("base64,").Last();
							byte[] bytes = Convert.FromBase64String(data);
							Stream stream = new MemoryStream(bytes);

							FileStream fs = new FileStream(localFileName, FileMode.Create, FileAccess.Write, FileShare.Write);
							stream.CopyTo(fs);
							fs.Close();
							fs.Dispose();

							// Libera a stream
							stream.Dispose();
						}
					}
					else
					{
						// Download the actual attachment
						using (var webClient = new WebClient())
						{
							Uri uri = new Uri(contentUrl);
							webClient.DownloadFileAsync(uri, localFileName);
						}

					}

					filename = file.Name;
				}

			}

			return filename;
		}
		// Valida um Email
		public static bool IsValidEmail(string email)
		{
			return Regex.IsMatch(email, @"^([\w-\.]+)@((\[[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.)|(([\w-]+\.)+))([a-zA-Z]{2,4}|[0-9]{1,3})(\]?)$");
		}
		// Valida um Celular
		public static bool IsValidPhone(string phone)
		{
			return Regex.IsMatch(phone, @"^1\d\d(\d\d)?$|^0800 ?\d{3} ?\d{4}$|^(\(0?([1-9a-zA-Z][0-9a-zA-Z])?[1-9]\d\) ?|0?([1-9a-zA-Z][0-9a-zA-Z])?[1-9]\d[ .-]?)?(9|9[ .-])?[2-9]\d{3}[ .-]?\d{4}$") & ClearStringNumber(phone).Length >= 11;
		}
		// Formata um Celular - verifica se tem 9 digitos + DDD + DDI - e nao é nextel ( 77, 78, 79, 70 ) nem fixo ( 2, 3, 4, 5 )
		// Se for Brasil, retira o 55 da frente
		public static string FormataCelular(string phone)
		{
			phone = ClearStringNumber(phone);
			if (phone.Substring(0, 2) == "55" & phone.Length >= 12)
				if (phone.Length == 12 & phone.Substring(5, 2) != "70" & phone.Substring(5, 2) != "77" & phone.Substring(5, 2) != "78" & phone.Substring(5, 2) != "79" & phone.Substring(5, 1) != "2" & phone.Substring(5, 1) != "3" & phone.Substring(5, 1) != "4" & phone.Substring(5, 1) != "5")
					return phone.Substring(2, 2) + "9" + phone.Substring(4);
				else
					return phone.Substring(2);
			else
				return phone;
		}
		// Verifica se o numero é um CPF - não faz analise do digito verificador, so confere o formato
		public static bool LooksLikeValidCPF(string cpf)
		{
			cpf = cpf.Replace(".", "").Replace("/", "").Replace("-", "").Replace(" ", "").Replace("(", "").Replace(")", "");
			return ClearStringNumber(cpf).Length == 11;
		}
		// Verifica se foi digitado um CNPJ - não faz analise do digito verificador, só confere o formato
		public static bool LooksLikeValidCNPJ(string cnpj)
		{
			cnpj = cnpj.Replace(".", "").Replace("/", "").Replace("-", "").Replace(" ", "").Replace("(", "").Replace(")", "");
			return ClearStringNumber(cnpj).Length == 14;
		}
		// Valida um CNJP - com digitos verificadores
		public static bool IsCnpj(string cnpj)
		{
			int[] multiplicador1 = new int[12] { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
			int[] multiplicador2 = new int[13] { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
			int soma;
			int resto;
			string digito;
			string tempCnpj;
			cnpj = cnpj.Trim();
			cnpj = cnpj.Replace(".", "").Replace("-", "").Replace("/", "");
			if (cnpj.Length != 14)
				return false;
			tempCnpj = cnpj.Substring(0, 12);
			soma = 0;
			for (int i = 0; i < 12; i++)
				soma += int.Parse(tempCnpj[i].ToString()) * multiplicador1[i];
			resto = (soma % 11);
			if (resto < 2)
				resto = 0;
			else
				resto = 11 - resto;
			digito = resto.ToString();
			tempCnpj += digito;
			soma = 0;
			for (int i = 0; i < 13; i++)
				soma += int.Parse(tempCnpj[i].ToString()) * multiplicador2[i];
			resto = (soma % 11);
			if (resto < 2)
				resto = 0;
			else
				resto = 11 - resto;
			digito += resto.ToString();
			return cnpj.EndsWith(digito);
		}
		// Valida um CPF - com digito verificador
		public static bool IsCpf(string cpf)
		{
			int[] multiplicador1 = new int[9] { 10, 9, 8, 7, 6, 5, 4, 3, 2 };
			int[] multiplicador2 = new int[10] { 11, 10, 9, 8, 7, 6, 5, 4, 3, 2 };
			string tempCpf;
			string digito;
			int soma;
			int resto;
			cpf = cpf.Trim();
			cpf = cpf.Replace(".", "").Replace("-", "");
			if (cpf.Length != 11)
				return false;
			tempCpf = cpf.Substring(0, 9);
			soma = 0;

			for (int i = 0; i < 9; i++)
				soma += int.Parse(tempCpf[i].ToString()) * multiplicador1[i];
			resto = soma % 11;
			if (resto < 2)
				resto = 0;
			else
				resto = 11 - resto;
			digito = resto.ToString();
			tempCpf = tempCpf + digito;
			soma = 0;
			for (int i = 0; i < 10; i++)
				soma += int.Parse(tempCpf[i].ToString()) * multiplicador2[i];
			resto = soma % 11;
			if (resto < 2)
				resto = 0;
			else
				resto = 11 - resto;
			digito = digito + resto.ToString();
			return cpf.EndsWith(digito);
		}
		// Limpa um nome de possível saudação
		public static string CleanName(string name)
		{
			name = name.ToUpperInvariant().Replace("MEU NOME É ", "").Replace("MEU NOME E ", "").Replace("EU SOU O ", "").Replace("SOU O ", "").Replace("ME CHAMO ", "").Replace("SOU ", "").Replace("AQUI É ","").Replace("AQUI E ", "").Replace("AQUI ", "");

			return name;
		}
		// Valida um Nome
		public static bool IsValidName(string name)
		{
			name = name.ToLower();
			if (name.Length < 2 || name == "oi" || name == "nao" || name.StartsWith("não") || name == "cancelar" || name == "sair" || name == "voltar" || name == "menu" || name == "cancel" || name == "*cancelar*" || name.StartsWith("oi ") || name.StartsWith("sim ") || name == "sim" || name.Contains("gostaria") || name.Contains("testar"))
				return false;
			else
				return Regex.IsMatch(name, @"^[\p{L}\p{M}' \.\-]+$");
		}
		// Cria um anexo com uma imagem salva
		public static Attachment CreateImageAttachment(string filename, Uri botUrl)
		{
			string fullfilename = Path.Combine(Path.Combine(Environment.CurrentDirectory, @"wwwroot\images"), filename);
			string filetype = Path.GetExtension(fullfilename).Replace(".", "");

			return new Attachment
			{
				Name = filename,
				ContentType = $"image/{filetype}",
				ContentUrl = Utility.UrlCombine(botUrl, @"/images/" + filename)
			};
		}
		// Cria um anexo com um PDF Salvo
		public static Attachment CreatePDFAttachment(string filename, Uri fileContainerUrl)
		{

			return new Attachment
			{
				Name = filename,
				ContentType = $"application/pdf",
				ContentUrl = Utility.UrlCombine(fileContainerUrl,  filename)
			};
		}
		public static Attachment CreateInlineAttachment(string filename)
		{
			string fullfilename = Path.Combine(Path.Combine(Environment.CurrentDirectory, "wwwroot"), filename);
			string filetype = Path.GetExtension(fullfilename).Replace(".", "");
			var fileData = Convert.ToBase64String(File.ReadAllBytes(fullfilename));

			return new Attachment
			{
				Name = filename,
				ContentType = $"image/{filetype}",
				ContentUrl = $"data:image/{filetype};base64,{fileData}",
			};
		}
		// Remove separadores de um numero como string
		public static string ClearStringNumber(string stringnumber)
		{
			if (string.IsNullOrEmpty(stringnumber))
				return string.Empty;
			else
				return stringnumber.Replace(" ", "").Replace(".", "").Replace("(", "").Replace(")", "").Replace("-", "").Replace("+", "").Replace("/", "");
		}
		// Hora do brasil
		public static DateTime HoraLocal()
		{
			int fusohorario = -3;
			string displayName = "(GMT-03:00) Brasília";
			string standardName = "Horário de Brasília";
			TimeSpan offset = new TimeSpan(fusohorario, 00, 00);
			TimeZoneInfo tzBrasilia = TimeZoneInfo.CreateCustomTimeZone(standardName, offset, displayName, standardName);
			return TimeZoneInfo.ConvertTime(DateTime.Now, TimeZoneInfo.Local, tzBrasilia);
		}
		// Valida um CEP - tem que estar formatado 00000-000
		public static bool IsValidCEP(string cep)
		{
			Regex Rgx = new Regex(@"^\d{5}-\d{3}$");

			return Rgx.IsMatch(cep);
		}
		// Formata um CEP - 00000-000
		public static string FormataCEP(string cep)
		{
			string fCep = ClearStringNumber(cep);
			if (fCep.Length == 8)
				fCep = fCep.Substring(0, 5) + "-" + fCep.Substring(5);
			return fCep;
		}
		// Primeira palavra do nome
		public static string FirstName(string fullName)
		{
			if (string.IsNullOrEmpty(fullName))
				return string.Empty;
			else
			{
				var names = fullName.Split(' ');
				return names[0];
			}
		}
		// Remove acentos
		public static string RemoveAcentos(this string texto)
		{
			if (string.IsNullOrEmpty(texto))
				return String.Empty;

			byte[] bytes = System.Text.Encoding.GetEncoding("iso-8859-8").GetBytes(texto);
			return System.Text.Encoding.UTF8.GetString(bytes);
		}
		public static string UrlCombine(Uri baseurl, string pathandfile)
		{
			pathandfile = pathandfile.TrimStart('/');
			return string.Format(new CultureInfo("en-US"), "{0}/{1}", baseurl.ToString().TrimEnd('/'), pathandfile);
		}
		public static string GetStrDateFromLuisSpec(DateTimeSpec dateTimeSpec)
		{
			//Confere se veio o dia do mes
			if (dateTimeSpec.Expressions[0].StartsWith("XXXX-XX-"))
			{
				if (int.TryParse(dateTimeSpec.Expressions[0].Substring(8), out int dia))
				{
					// Se o dia é maior do que o dia de hoje - estamos falando deste mês
					if (dia > DateTime.Today.Day)

						// Devolve uma string o dia digitado e o mes atual
						return dia.ToString() + "/" + DateTime.Today.Month.ToString();
					else
						// Devolve uma string com o dia digitado, o mes seguinte e ano
						return dia.ToString() + "/" + DateTime.Today.AddMonths(1).Month + "/" + DateTime.Today.AddMonths(1).Year;
				}
				else
					// dateSpec invalida
					return string.Empty;
			}
			//Confere se veio o dia da semana
			else if (dateTimeSpec.Expressions[0].StartsWith("XXXX-WXX-"))
			{
				if (int.TryParse(dateTimeSpec.Expressions[0].Substring(9), out int weekday))
				{
					// Busca o proxima data que corresponde ao dia da semana informado
					if (weekday > (int)DateTime.Today.DayOfWeek)
						return DateTime.Today.AddDays(weekday - (int)DateTime.Today.DayOfWeek).ToString("dd/MM/yyyy");

					else
						// Devolve uma string com o dia digitado, o mes seguinte e ano
						return DateTime.Today.AddDays(weekday - (int)DateTime.Today.DayOfWeek + 7).ToString("dd/MM/yyyy");
				}
				else
					// dateSpec invalida
					return string.Empty;
			}
			//Confere se veio mes e dia
			else if (dateTimeSpec.Expressions[0].StartsWith("XXXX-"))
			{
				if (int.TryParse(dateTimeSpec.Expressions[0].Substring(8), out int dia))
				{
					if (int.TryParse(dateTimeSpec.Expressions[0].Substring(5, 2), out int mes))
					{
						// Se o mes é maior ou igual ao mes atual
						if (mes >= DateTime.Today.Month)
							// Devolve uma string com a dia, mes e ano atual
							return dia.ToString() + "/" + mes.ToString() + "/" + DateTime.Today.Year.ToString();

						else
							// Devolve uma string com o dia digitado, o mes seguinte e ano que vem
							return dia.ToString() + "/" + mes.ToString() + "/" + DateTime.Today.AddYears(1).Year.ToString();
					}
					else
						// dateSpec invalida
						return string.Empty;

				}
				else
					// dateSpec invalida
					return string.Empty;
			}
			else
				// dateSpec invalida
				return string.Empty;
		}

		public static string GetNumberFromLuis( MisterBotLuis misterBotLuis)
		{

			for ( int x=0;  x<= misterBotLuis.Entities.number.Length-1; x++)
			{
				if (misterBotLuis.Entities.number[x].ToString(CultureInfo.InvariantCulture) == misterBotLuis.Entities._instance.number[x].Text)
					return misterBotLuis.Entities._instance.number[x].Text;
			}

			return string.Empty;
		}
		public static string CleanUtterance(string userinput)
		{
			userinput = userinput.Replace("Susie", "").Replace("Susi", "").Replace("Suzi", "").Replace("Suzy", "").Replace("Susy", "").Replace("Susie", "").Replace("Suso", "").Replace("!", " ").Trim();

			if (userinput.Length > 0 && userinput.Substring(userinput.Length - 1) == ".")
				userinput = userinput.Substring(0, userinput.Length - 1);

			return userinput.Trim();
		}
		public static string BomTurno()
		{
			return HoraLocal().Hour > 19 ? "Boa noite!" : Utility.HoraLocal().Hour > 12 ? "Boa tarde!" : Utility.HoraLocal().Hour > 5 ? "Bom dia!" : "Boa noite!";
		}
		// Confere se o contexto atual é pai do diálogo indicado
		public static bool DialogIsRunning(DialogContext innerDc, string subdialogname)
		{

			if (innerDc.ActiveDialog.Id == subdialogname)
				return true;

			bool haschild = false;

			while (innerDc.Child != null)
			{
				if (innerDc.Child.ActiveDialog.Id == subdialogname)
					haschild = true;
				innerDc = innerDc.Child;
			}
			return haschild;
		}
		// Return if a date is Holiday
		public static bool IsHoliday(DateTime date)
		{
			var Holidays = new List<DateTime>();

			// Fixos no Brasil
			Holidays.Add(new DateTime(DateTime.Now.Year, 1, 1));	// Ano Novo
			Holidays.Add(new DateTime(DateTime.Now.Year, 4, 21));   // Tira Dentes
			Holidays.Add(new DateTime(DateTime.Now.Year, 5, 1));    // Dia do trabalho
			Holidays.Add(new DateTime(DateTime.Now.Year, 9, 7));	// 7 de Setembro
			Holidays.Add(new DateTime(DateTime.Now.Year, 10, 12));  // Nossa Senhora Aparecida
			Holidays.Add(new DateTime(DateTime.Now.Year, 11, 2));   // Finados
			Holidays.Add(new DateTime(DateTime.Now.Year, 11, 15));  // Proclamação da República
			Holidays.Add(new DateTime(DateTime.Now.Year, 12, 25));  // Natal

			// Moveis Brasil 2021
			Holidays.Add(new DateTime(2021, 2, 15));   // Carnaval
			Holidays.Add(new DateTime(2021, 2, 16));   // Carnaval
			Holidays.Add(new DateTime(2021, 2, 17));   // Carnaval
			Holidays.Add(new DateTime(2021, 4, 2));    // Sexta feira santa

			return Holidays.Contains(date);
		}
		// Return true or false if a date is weend or not
		public static bool IsWeekend(DateTime date)
		{
			return date.DayOfWeek == DayOfWeek.Saturday
				|| date.DayOfWeek == DayOfWeek.Sunday;
		}
		// Return next business day after given date
		public static DateTime GetNextWorkingDay(DateTime date)
		{
			do
			{
				date = date.AddDays(1);
			} while (IsHoliday(date) || IsWeekend(date));
			return date;
		}

	}
}

