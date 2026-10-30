using System.Text.Json;
using BeafSoup.Content;

namespace BeafSoup.AudioGenerator;

public sealed class AudioGenerationRunner(
    ContentSnapshot content,
    IAudioSynthesizer synthesizer,
    TtsOptions options,
    string audioRoot)
{
    private readonly string _languageRoot = Path.Combine(audioRoot, content.Course.TargetLanguage);
    private readonly string _manifestPath = Path.Combine(audioRoot, content.Course.TargetLanguage, "manifest.json");

    private string SettingsHashFor(AudioSourceItem job) =>
        AudioFingerprint.SettingsHash(
            options.Model, job.Voice ?? options.Voice, options.ResponseFormat, options.Language, job.Style);

    public async Task<GenerationReport> RunAsync(string? onlyId, bool force, bool dryRun, CancellationToken cancellationToken)
    {
        var jobs = content.GetAudioJobs();
        if (onlyId is not null)
        {
            jobs = jobs.Where(job => job.SourceId == onlyId).ToList();
            if (jobs.Count == 0)
            {
                throw new ArgumentException($"找不到音频资源：{onlyId}");
            }
        }

        Directory.CreateDirectory(_languageRoot);
        var loader = new JsonContentLoader();
        var manifest = loader.LoadAudioManifest(_manifestPath);
        manifest.Provider = options.Provider;
        manifest.Model = options.Model;
        manifest.Language = content.Course.TargetLanguage;
        var report = new GenerationReport(jobs.Count);

        foreach (var job in jobs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = manifest.Entries.FirstOrDefault(entry => entry.SourceId == job.SourceId);
            var expectedExtension = options.ResponseFormat == "wav" ? ".wav" : ".mp3";
            var relativePath = $"{job.Kind}/{job.SourceId}{expectedExtension}";
            var fullPath = Path.Combine(_languageRoot, relativePath);
            var sourceHash = AudioFingerprint.Hash(job.Text);
            var settingsHash = SettingsHashFor(job);

            if (!force && CanSkip(existing, job, sourceHash, settingsHash, fullPath))
            {
                report.Skipped++;
                Console.WriteLine($"跳过 {job.SourceId}：现有音频仍然有效。");
                continue;
            }

            if (dryRun)
            {
                report.Pending++;
                Console.WriteLine($"待生成 {job.SourceId} [{job.Kind}] {job.Text}");
                continue;
            }

            try
            {
                var data = await GenerateWithRetryAsync(job, cancellationToken);
                var actualRelativePath = $"{job.Kind}/{job.SourceId}{data.Extension}";
                var actualFullPath = Path.Combine(_languageRoot, actualRelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(actualFullPath)!);
                await RetryFileOperationAsync(() => WriteAtomicallyAsync(actualFullPath, data.Bytes, cancellationToken), cancellationToken);

                ReplaceEntry(manifest, new AudioManifestEntry
                {
                    SourceId = job.SourceId,
                    Kind = job.Kind,
                    RelativePath = actualRelativePath,
                    SourceTextSha256 = sourceHash,
                    SettingsSha256 = settingsHash,
                    ContentType = data.ContentType,
                    ByteLength = data.Bytes.LongLength,
                    Status = "ready",
                    GeneratedAtUtc = DateTimeOffset.UtcNow
                });
                await RetryFileOperationAsync(() => SaveManifestAsync(manifest, cancellationToken), cancellationToken);
                report.Generated++;
                Console.WriteLine($"完成 {job.SourceId}：{data.Bytes.Length} 字节。");
            }
            catch (Exception exception) when (exception is TtsException or HttpRequestException or IOException or UnauthorizedAccessException)
            {
                var error = exception.Message;
                ReplaceEntry(manifest, new AudioManifestEntry
                {
                    SourceId = job.SourceId,
                    Kind = job.Kind,
                    RelativePath = relativePath,
                    SourceTextSha256 = sourceHash,
                    SettingsSha256 = settingsHash,
                    Status = "failed",
                    GeneratedAtUtc = DateTimeOffset.UtcNow,
                    Error = error
                });
                await RetryFileOperationAsync(() => SaveManifestAsync(manifest, cancellationToken), cancellationToken);
                report.Failed++;
                Console.Error.WriteLine($"失败 {job.SourceId}：{error}");
            }
        }

        return report;
    }

    private async Task<AudioData> GenerateWithRetryAsync(AudioSourceItem job, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Console.WriteLine($"生成 {job.SourceId}（第 {attempt} 次）...");
                return await synthesizer.SynthesizeAsync(
                    new AudioSynthesisRequest(job.Text, job.Voice ?? options.Voice, job.Style, job.Rate ?? "1.0"),
                    cancellationToken);
            }
            catch (TtsException exception) when (exception.Transient && attempt < options.MaxAttempts)
            {
                await Task.Delay(RetryDelay(exception, attempt), cancellationToken);
            }
            catch (HttpRequestException) when (attempt < options.MaxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1)), cancellationToken);
            }
        }
    }

    private static TimeSpan RetryDelay(TtsException exception, int attempt)
    {
        if (exception.StatusCode == 429)
        {
            return TimeSpan.FromSeconds(Math.Min(30, 5 * Math.Pow(2, attempt - 1)));
        }

        return TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1));
    }

    private bool CanSkip(AudioManifestEntry? entry, AudioSourceItem job, string sourceHash, string settingsHash, string expectedPath)
    {
        if (entry is null || entry.Status != "ready" || entry.Kind != job.Kind ||
            entry.SourceTextSha256 != sourceHash || entry.SettingsSha256 != settingsHash ||
            !File.Exists(expectedPath))
        {
            return false;
        }

        var bytes = File.ReadAllBytes(expectedPath);
        return bytes.LongLength == entry.ByteLength &&
            AudioValidator.DetectExtension(bytes, entry.ContentType) == Path.GetExtension(expectedPath);
    }

    private static void ReplaceEntry(AudioManifest manifest, AudioManifestEntry entry)
    {
        manifest.Entries.RemoveAll(item => item.SourceId == entry.SourceId);
        manifest.Entries.Add(entry);
        manifest.Entries = manifest.Entries.OrderBy(item => item.Kind).ThenBy(item => item.SourceId).ToList();
    }

    private static async Task WriteAtomicallyAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        var temporaryPath = path + ".tmp";
        await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
        File.Move(temporaryPath, path, true);
    }

    private static async Task RetryFileOperationAsync(Func<Task> operation, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await operation();
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt == 5) throw;
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
            }
        }
    }

    private async Task SaveManifestAsync(AudioManifest manifest, CancellationToken cancellationToken)
    {
        var temporaryPath = _manifestPath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, manifest, JsonContentLoader.SerializerOptions, cancellationToken);
        }

        File.Move(temporaryPath, _manifestPath, true);
    }
}

public sealed record GenerationReport(int Total)
{
    public int Generated { get; set; }
    public int Skipped { get; set; }
    public int Pending { get; set; }
    public int Failed { get; set; }
}
