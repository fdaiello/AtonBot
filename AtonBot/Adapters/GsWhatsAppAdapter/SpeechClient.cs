using Concentus.Oggfile;
using Concentus.Structs;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GsWhatsAppAdapter
{
	// Utility Methods
	public class SpeechClient
	{
		// Variaveis lidas de AppSettings
		private readonly string SpeechSubcriptionKey;
		private readonly string SpeechRegion;
		private readonly string Language;

		// Logger
		private readonly ILogger _logger;

		// Constructor
		public SpeechClient(SpeechOptions options, ILogger logger)
		{
			if (options == null)
			{
				throw new ArgumentNullException(nameof(options));
			}
			SpeechSubcriptionKey = options.SpeechSubcriptionKey;
			SpeechRegion = options.SpeechRegion;
			Language = options.Language;
			_logger = logger;
		}

		// Reconhece um Audio Stream no formato Ogg e devolve o texto
		public async Task<string> RecognizeOggStream(Stream stream, String voiceid)
		{
			// valida parametro stream
			if (stream == null)
				return string.Empty;

			// Se o diretorio wwwroot\medias nao exite, cria
			if (!Directory.Exists(Path.Combine(Environment.CurrentDirectory, $@"wwwroot\media\")))
				Directory.CreateDirectory(Path.Combine(Environment.CurrentDirectory, $@"wwwroot\media\"));

			// Cria o nome do arquivo
			string filename = Path.Combine(Environment.CurrentDirectory, $@"wwwroot\media\Audio_{voiceid}.ogg");
			// Salva o arquivo
			FileStream fs = new FileStream(filename, FileMode.Create, FileAccess.Write, FileShare.Write);
			stream.CopyTo(fs);
			fs.Close();
			fs.Dispose();

			// gera novo nome para salvar nova versão convertido pra Wave
			string filename2 = filename.Replace(".ogg", ".wav", StringComparison.InvariantCultureIgnoreCase);

			// se ja existe arquivo com este nome ... 
			if (File.Exists(filename2))
				// .. apaga
				File.Delete(filename2);

			// Converte Ogg para Wav
			ConvertOggToWav(filename, filename2);

			// Reconhece e devolve o audio
			return await SpeechToTextAsync(filename2).ConfigureAwait(false);
		}

		// Converte OGG para Wav
		private static void ConvertOggToWav(string inFilename, string outFilename)
		{

			// Le o arquivo salvo em Ogg - usando Concentus.Structs.OpusDecoder
			using FileStream fileIn = new FileStream(inFilename, FileMode.Open);
			using MemoryStream pcmStream = new MemoryStream();
			OpusDecoder decoder = OpusDecoder.Create(48000, 1);
			OpusOggReadStream oggIn = new OpusOggReadStream(decoder, fileIn);
			while (oggIn.HasNextPacket)
			{
				short[] packet = oggIn.DecodeNextPacket();
				if (packet != null)
				{
					for (int i = 0; i < packet.Length; i++)
					{
						var oggbytes = BitConverter.GetBytes(packet[i]);
						pcmStream.Write(oggbytes, 0, oggbytes.Length);
					}
				}
			}

			// Salva no formato WAV
			pcmStream.Position = 0;
			var wavStream = new RawSourceWaveStream(pcmStream, new WaveFormat(48000, 1));
			var sampleProvider = wavStream.ToSampleProvider();
			WaveFileWriter.CreateWaveFile16(outFilename, sampleProvider);
			wavStream.Dispose();

		}

		// Speech Recognition
		private async Task<string> SpeechToTextAsync(string filename)
		{
			// variavel de configuração do serviço de Speech Recognition do Azure
			var config = SpeechConfig.FromSubscription(SpeechSubcriptionKey, SpeechRegion);

			// Carrega o arquivo de audio
			using var audioInput = AudioConfig.FromWavFileInput(filename);

			// Carrega o objeto de reconhecimento - com o audio e a configuracao da assinatura
			var sourceLanguageConfig = SourceLanguageConfig.FromLanguage("pt-BR");
			using var recognizer = new SpeechRecognizer(config, sourceLanguageConfig, audioInput);
			var result = await recognizer.RecognizeOnceAsync().ConfigureAwait(false);

			switch (result.Reason)
			{
				case ResultReason.RecognizedSpeech:
					return WrittenNroToDigit(result.Text);

				case ResultReason.NoMatch:
					return $"NOMATCH";

				case ResultReason.Canceled:
					var cancellation = CancellationDetails.FromResult(result);
					return $"CANCELED: Reason={cancellation.Reason}, {cancellation.ErrorCode}, {cancellation.ErrorDetails}";

				default:
					return string.Empty;

			}

		}
		// Converte numero escrito para numeral
		private static string WrittenNroToDigit(string inline)
		{
			if (inline == "Um.")
				inline = "1";

			else if (inline.Replace(".", "", StringComparison.InvariantCulture).All(char.IsDigit))
				inline = inline.Replace(".", "", StringComparison.InvariantCulture);

			return (inline);
		}

		// Speech synthesis in the specified spoken language.
		public async Task<bool> TextToSpeechAsync(string text, string voiceid)
		{

			if (!string.IsNullOrEmpty(text))
			{
				// variavel de configuração do serviço de Speech Recognition do Azure
				var config = SpeechConfig.FromSubscription(SpeechSubcriptionKey, SpeechRegion);

				// Sets the synthesis language.
				// https://docs.microsoft.com/azure/cognitive-services/speech-service/language-support
				config.SpeechSynthesisLanguage = Language;

				// Sets the voice name.
				// e.g. "Microsoft Server Speech Text to Speech Voice (en-US, JessaRUS)"
				// The full list of supported voices can be found here:
				// Feminino: "pt-BR-HeloisaRUS"
				// Masculino: "pt-BR-Daniel-Apollo"
				// https://docs.microsoft.com/azure/cognitive-services/speech-service/language-support
				//var voice = "Microsoft Server Speech Text to Speech Voice (pt-BR, Daniel-Apollo)";
				//config.SpeechSynthesisVoiceName = voice;

				// Cria o nome do arquivo
				string filename = Path.Combine(Environment.CurrentDirectory, $@"wwwroot\media\Audio_{voiceid}.wav");

				using var fileOutput = AudioConfig.FromWavFileOutput(filename);

				// Creates a speech synthesizer for the specified language, using the default speaker as audio output.
				using var synthesizer = new SpeechSynthesizer(config, fileOutput);
				using var result = await synthesizer.SpeakTextAsync(text).ConfigureAwait(false);

				if (result.Reason == ResultReason.SynthesizingAudioCompleted)
				{
					return (true);
				}
				else if (result.Reason == ResultReason.Canceled)
				{
					var cancellation = SpeechSynthesisCancellationDetails.FromResult(result);
					_logger.LogError($"CANCELED: Reason={cancellation.Reason}");

					if (cancellation.Reason == CancellationReason.Error)
					{
						_logger.LogError($"CANCELED: ErrorCode={cancellation.ErrorCode}");
						_logger.LogError($"CANCELED: ErrorDetails=[{cancellation.ErrorDetails}]");
					}
					return (false);
				}
				else
					return (false);

			}
			else
				return (false);

		}

	}
}

