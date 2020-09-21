using MrBot.Models;
using System;
using System.Collections.Generic;

namespace MrBot
{
	// Defines a state property used to track conversation data.
	public class ConversationData
	{
		// Last question
		public string FirstQuestion { get; set; }

		// Last service
		public string Service { get; set; }

		public int AskPriceCount { get; set; } = 0;

		// Customer Info
		public Customer Customer { get; set; }

		// Number of intent recognition failed attempts
		public int IntentNotRecognized { get; set; } = 0;

		// NextAvailableDates 
		public List<DateTime> NextAvailableDates { get; } = new List<DateTime>();

		public void AddAvailableDate( DateTime availabledate)
		{
			this.NextAvailableDates.Add(availabledate);
		}
		// Mark if proposal was sent
		public bool PropostaEnviada { get; set; }
		// Mark if bill was sent
		public bool BoletoEnviado { get; set; }
		// Marca se já informou os técnicos
		public bool TecnicosInstalacaoInformado { get; set; }
	}
}
