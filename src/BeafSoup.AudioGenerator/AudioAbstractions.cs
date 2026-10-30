namespace BeafSoup.AudioGenerator;

public sealed class TtsOptions
{
    public string Provider { get; set; } = "nvidia";
    public string BaseUrl { get; set; } = "https://integrate.api.nvidia.com/v1/";
    public string Model { get; set; } = "nvidia/magpie-tts-zeroshot";
    public string Voice { get; set; } = "auto";
    public string ResponseFormat { get; set; } = "mp3";
    public string Language { get; set; } = "de-DE";
    public int MaxAttempts { get; set; } = 6;
}

public sealed record AudioData(byte[] Bytes, string ContentType, string Extension);

/// <summary>One synthesis request. Voice, style and rate come from the content item when set
/// (dialogue speakers), otherwise from the provider options.</summary>
public sealed record AudioSynthesisRequest(string Text, string Voice, string? Style, string Rate);

public interface IAudioSynthesizer
{
    Task<AudioData> SynthesizeAsync(AudioSynthesisRequest request, CancellationToken cancellationToken);
}

public sealed class TtsException(string message, int? statusCode = null, bool transient = false)
    : Exception(message)
{
    public int? StatusCode { get; } = statusCode;
    public bool Transient { get; } = transient;
}
