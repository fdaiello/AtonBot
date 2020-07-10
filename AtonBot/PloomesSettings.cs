using System;

namespace PloomesApi
{
	public class PloomesSettings
	{
		public PloomesSettings(string userKey, Uri serverUri)
		{
			UserKey = userKey;
			ServerUri = serverUri;
		}
		public string UserKey { get; set; }

		public Uri ServerUri { get; set; }
	}
}
