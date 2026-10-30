using BeafSoup.Content;
using BeafSoup.Contracts;

namespace BeafSoup.Api;

public sealed class AudioUrlResolver(
    ContentSnapshot content,
    AudioManifest manifest,
    string audioLanguageRoot)
{
    private readonly Dictionary<string, AudioManifestEntry> _entries =
        manifest.Entries.ToDictionary(entry => entry.SourceId);

    public string? Resolve(AudioSourceItem source)
    {
        if (!_entries.TryGetValue(source.SourceId, out var entry) ||
            entry.Status != "ready" ||
            entry.Kind != source.Kind ||
            entry.SourceTextSha256 != AudioFingerprint.Hash(source.Text) ||
            string.IsNullOrWhiteSpace(entry.RelativePath))
        {
            return null;
        }

        var relativePath = entry.RelativePath.Replace('\\', '/');
        var fullPath = Path.GetFullPath(Path.Combine(audioLanguageRoot, relativePath));
        var expectedRoot = Path.GetFullPath(audioLanguageRoot)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(fullPath) ||
            new FileInfo(fullPath).Length != entry.ByteLength)
        {
            return null;
        }

        return $"/audio/{content.Course.TargetLanguage}/{relativePath}";
    }
}
