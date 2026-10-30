using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace BeafSoup.AudioGenerator;

public sealed class NvidiaTtsClient(HttpClient httpClient, TtsOptions options) : IAudioSynthesizer
{
    public async Task<AudioData> SynthesizeAsync(AudioSynthesisRequest request, CancellationToken cancellationToken)
    {
        var requestBody = JsonSerializer.Serialize(new
        {
            model = options.Model,
            input = request.Text,
            voice = request.Voice,
            response_format = options.ResponseFormat
        });

        using var message = new HttpRequestMessage(HttpMethod.Post, "audio/speech")
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
        };
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/mpeg"));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/wav"));

        using var response = await httpClient.SendAsync(message, cancellationToken);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            var transient = status == 408 || status == 429 || status >= 500;
            var detail = SafeDetail(bytes);
            throw new TtsException($"NVIDIA TTS 返回 HTTP {status}：{detail}", status, transient);
        }

        var extension = AudioValidator.DetectExtension(bytes, contentType);
        return new AudioData(bytes, contentType, extension);
    }

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

public static class AudioValidator
{
    public static string DetectExtension(byte[] bytes, string contentType)
    {
        if (bytes.Length < 32 || contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) ||
            contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
        {
            throw new TtsException("语音服务未返回有效的音频内容。", 200);
        }

        if (bytes.Length >= 12 &&
            bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F' &&
            bytes[8] == (byte)'W' && bytes[9] == (byte)'A' && bytes[10] == (byte)'V' && bytes[11] == (byte)'E')
        {
            return ".wav";
        }

        var hasId3 = bytes[0] == (byte)'I' && bytes[1] == (byte)'D' && bytes[2] == (byte)'3';
        var hasMpegFrame = bytes[0] == 0xFF && (bytes[1] & 0xE0) == 0xE0;
        if (hasId3 || hasMpegFrame)
        {
            return ".mp3";
        }

        throw new TtsException("响应内容既不是有效的 MP3，也不是有效的 WAV。", 200);
    }
}
