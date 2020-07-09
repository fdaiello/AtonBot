// Options for Speach Regocnition Service
namespace GsWhatsAppAdapter
{
	public class SpeechOptions
	{
		public SpeechOptions(string speechSubcriptionKey, string speechRegion, string language)
		{
			SpeechSubcriptionKey = speechSubcriptionKey;
			SpeechRegion = speechRegion;
			Language = language;
		}
		public string SpeechSubcriptionKey { get; set; }
		public string SpeechRegion { get; set; }
		public string Language { get; set; }
	}
}
