using ContactCenter.Core.Models;
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

		public int AskPriceCount { get; set; }

		// Customer Info
		public Contact Customer { get; set; }

		// Number of intent recognition failed attempts
		public int IntentNotRecognized { get; set; }

		// NextAvailableDates 
		public List<DateTime> NextAvailableDates { get; } = new List<DateTime>();

		public void AddAvailableDate( DateTime availabledate)
		{
			this.NextAvailableDates.Add(availabledate);
		}
		public void ResetAvailableDates()
        {
			this.NextAvailableDates.Clear();
        }
		// Mark if proposal was sent
		public bool PropostaEnviada { get; set; }
		// Mark if bill was sent
		public bool BoletoEnviado { get; set; }
		// Marca se já informou os técnicos da visita
		public bool TecnicosVisitaInformado { get; set; }
		// Marca se já informou os técnicos da instalação
		public bool TecnicosInstalacaoInformado { get; set; }
	}
}
