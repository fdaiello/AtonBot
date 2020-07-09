using Microsoft.Bot.Schema;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace GsWhatsAppAdapter
{

	public class HeroCard
	{
		[JsonProperty(PropertyName = "title")]
		public string Title { get; set; }
		[JsonProperty(PropertyName = "subtitle")]
		public string Subtitle { get; set; }
		[JsonProperty(PropertyName = "text")]
		public string Text { get; set; }
		[JsonProperty(PropertyName = "images")]
#pragma warning disable CA2227									// Tem que manter {get; set;} nos tipos IList, caso contrario desserialização via Json não funciona;
		public IList<CardImage> Images { get; set; }
		[JsonProperty(PropertyName = "buttons")]
		public IList<CardAction> Buttons { get; set; }
#pragma warning restore CA2227
		[JsonProperty(PropertyName = "tap")]
		public CardAction Tap { get; set; }
	}
}
