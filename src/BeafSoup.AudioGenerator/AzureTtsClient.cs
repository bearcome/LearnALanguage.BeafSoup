using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;

namespace BeafSoup.AudioGenerator;

public sealed class AzureTtsClient(HttpClient httpClient, TtsOptions options, string apiKey) : IAudioSynthesizer
{
    public async Task<AudioData> SynthesizeAsync(AudioSynthesisRequest request, CancellationToken cancellationToken)
    {
        var style = string.IsNullOrWhiteSpace(request.Style) ? null : request.Style;
        var rate = string.IsNullOrWhiteSpace(request.Rate) ? "1.0" : request.Rate;
        var text = new XElement("prosody", new XAttribute("rate", rate), request.Text);
        XElement spoken = style is null
            ? text
            : new XElement(MsttsNamespace + "express-as", new XAttribute("style", style), text);
        var ssml = new XDocument(
            new XElement("speak",
                new XAttribute("version", "1.0"),
                new XAttribute(XNamespace.Xml + "lang", options.Language),
                new XAttribute(XNamespace.Xmlns + "mstts", MsttsNamespace.NamespaceName),
                new XElement("voice",
                    new XAttribute(XNamespace.Xml + "lang", options.Language),
                    new XAttribute("name", request.Voice),
                    spoken))).ToString(SaveOptions.DisableFormatting);

        using var message = new HttpRequestMessage(HttpMethod.Post, "")
        {
            Content = new StringContent(ssml, Encoding.UTF8, "application/ssml+xml")
        };
        message.Headers.Add("Ocp-Apim-Subscription-Key", apiKey);
        message.Headers.Add("X-Microsoft-OutputFormat", options.ResponseFormat == "wav"
            ? "riff-16khz-16bit-mono-pcm"
            : "audio-16khz-32kbitrate-mono-mp3");
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(
            options.ResponseFormat == "wav" ? "audio/wav" : "audio/mpeg"));

        using var response = await httpClient.SendAsync(message, cancellationToken);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            var transient = status == 408 || status == 429 || status >= 500;
            throw new TtsException($"Azure TTS 返回 HTTP {status}：{SafeDetail(bytes)}", status, transient);
        }

        var extension = AudioValidator.DetectExtension(bytes, contentType);
        return new AudioData(bytes, contentType, extension);
    }

    private static readonly XNamespace MsttsNamespace = "https://www.w3.org/2001/mstts";

    private static string SafeDetail(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return "空响应";
        }

        var text = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 512));
        return text.Replace('\r', ' ').Replace('\n', ' ');
    }
}
