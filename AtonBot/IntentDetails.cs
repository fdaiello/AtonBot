using Microsoft.Bot.Builder.AI.Luis;
using MrBot.CognitiveModels;

namespace MrBot
{
	public class IntentDetails
	{
		public MisterBotLuis.Intent Intent { get; set; }
		public string Servico { get; set; }
		public string Quantidade { get; set; }
		public string FormaPagamento { get; set; }
		public DateTimeSpec DataSpec { get; set; }
		public string Modalidade { get; set; }
		public string Atendente { get; set; }

	}
}
