using MrBot.Models;

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

	}
}
