using System;

namespace GsWhatsApp
{
	/// <summary>
	/// Defines values that a <see cref="GsWhatsAppClientWrapper"/> can use to connect to WhatsApp's using GupShup API.
	/// </summary>
	public class GsWhatsAppOptions
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="GsWhatsAppOptions"/> class.
		/// </summary>
		/// <param name="GsApiKey">The KEY for using the API from GupShup Account.</param>
		/// <param name="GsApiUri">URI for API calls.</param>
		/// <param name="GsMediaUri">URI for retreaving media.</param>
		public GsWhatsAppOptions(string gsApiKey, Uri gsApiUri, Uri gsMediaUri)
		{
			GsApiKey = gsApiKey;
			GsApiUri = gsApiUri;
			GsMediaUri = gsMediaUri;
		}

		/// <summary>
		/// Gets or sets API KEY from the GupShup account.
		/// </summary>
		/// <value>The account SID.</value>
		public string GsApiKey { get; set; }

		/// <summary>
		/// Gets or sets the URI for the API calls
		/// </summary>
		public Uri GsApiUri { get; set; }

		/// <summary>
		/// Gets or sets the URL for getting media files
		/// </summary>
		public Uri GsMediaUri { get; set; }
	}
}
