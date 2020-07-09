// Common Emojis and Messages
// Used in Dialogs
namespace MrBot.Dialogs
{
	public class DialogDictionary
	{
		public Emoji Emoji { get; }
		public SharedMessage SharedMessage { get; }
		public SharedFiles SharedFiles { get; }
		public CultureInfoCodes CultureInfoCodes { get; }

		public DialogDictionary()
		{
			Emoji = new Emoji();
			SharedMessage = new SharedMessage();
			SharedFiles = new SharedFiles();
			CultureInfoCodes = new CultureInfoCodes();
		}

	}
	public class Emoji
	{
		public string InformationDeskPerson { get; } = "\U0001F481";
		public string GrinningFace { get; } = "\U0001F600";
		public string Play { get; } = "\U000025b6";
		public string ThumbsUp { get; } = "\U0001F44D";
		public string Smilingfacewithsmilingeyes { get; } = "\U0001F60A";
		public string LockedWithKey { get; } = "\U0001F510";
		public string Locked { get; } = "\U0001F512";
		public string OkHand { get; } = "\U0001F44C";
		public string SlotMachine { get; } = "\U0001F3B0";
		public string Key { get; } = "\U0001F511";

		public string Moon1 { get; } = "\U0001F311";
		public string Moon2 { get; } = "\U0001F318";
		public string Moon3 { get; } = "\U0001F317";
		public string Moon4 { get; } = "\U0001F316";
		public string Moon5 { get; } = "\U0001F315";
		public string BookmarkTabs { get; } = "\U0001F4D1";

		public string DolarBankNote { get; } = "\U0001F4B5";
		public string DisapointedFace { get; } = "\U0001F61E";

		public string MoneyBag { get; } = "\U0001F4B0";

		public string CheckMarkButton { get; } = "\U00002705";
		public string Ticket { get; } = "\U0001F3AB";
		public string ManMechanic { get; } = "\U0001F527";

		public string Paperclip { get; } = "\U0001F4CE";
		public string PaperFacingUp { get; } = "\U0001F4C4";

		public string ThinkingFace { get; } = "\U0001F914";

		public string SlightlyFrowningFace { get; } = "\U0001F641";

		public string PersonShrugging { get; } = "\U0001F937";

		public string WomanTeacher { get; } = "\U0001F469";

		public string WomanTechnologist { get; } = "\U0001F469";

		public string LoudSpeaker { get; } = "\U0001F4E2";
		public string AlarmClock { get; } = "\U000023F0";

		public string SpeechBaloon { get; } = "\U0001F4AC";
		public string MobilePhoneWithArrow { get; } = "\U0001F4F2";

		public string Sparkle { get; } = "\U00002747";

		public string Person { get; } = "\U0001F9D1";

		public string RoundPushPin { get; } = "\U0001F4CD";
		public string ExclamationMark { get; } = "\U00002757";
	}

	public class SharedMessage
	{
		const string warning = "\U000026A0";

		public string MsgContinuar { get; } = warning + " tecle 'ok' para continuar";
		public string MsgLermais { get; } = warning + " tecle:'ok' para ler mais, ou 'menu' para outras opções";
		public string MsgVoltarMenu { get; } = warning + " tecle 'menu' para voltar";
		public string MsgAlgoMais { get; } = warning + " Se precisar de algo mais, é só pedir. Ou digite 'menu' que lhe mostro algumas opções.";
	}

	public class SharedFiles
	{
		public string BotProfilePicture { get; } = "Suzi-ProfilePicture.jpg";
		public string PriceListSMS { get; } = "Tabela_Precos_SMS_Mister_Postman.png";
		public string PriceListSMSPdf { get; } = "Tabela-de-Precos-SMS.pdf";
		public string PriceListEmail { get; } = "Tabela_Precos_Email_Mister_Postman.png";
		public string PriceListEmailPdf { get; } = "Tabela-de-Precos-Email.pdf";
	}
	public class CultureInfoCodes
	{
		public string PtBr { get; } = "pt-BR";
	}
}
