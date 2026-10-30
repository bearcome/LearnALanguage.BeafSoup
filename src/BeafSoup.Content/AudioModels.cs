using System.Security.Cryptography;
using System.Text;

namespace BeafSoup.Content;

public sealed record AudioSourceItem(string SourceId, string Kind, string Text, string? Voice = null, string? Style = null, string? Rate = null);

public sealed class AudioManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string Language { get; set; } = "de-DE";
    public string Provider { get; set; } = "nvidia";
    public string Model { get; set; } = "nvidia/magpie-tts-zeroshot";
    public List<AudioManifestEntry> Entries { get; set; } = [];
}

public sealed class AudioManifestEntry
{
    public string SourceId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string SourceTextSha256 { get; set; } = "";
    public string SettingsSha256 { get; set; } = "";
    public string ContentType { get; set; } = "audio/mpeg";
    public long ByteLength { get; set; }
    public string Status { get; set; } = "ready";
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public string? Error { get; set; }
}

public static class AudioFingerprint
{
    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static string SettingsHash(string model, string voice, string format, string language, string? style = null) =>
        Hash($"{model}\n{voice}\n{format}\n{language}\n{style ?? ""}");
}
