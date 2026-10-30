using BeafSoup.AudioGenerator;
using BeafSoup.Content;

if (args.Contains("--help") || args.Contains("-h"))
{
    PrintHelp();
    return 0;
}

try
{
    var dryRun = args.Contains("--dry-run");
    var force = args.Contains("--force");
    var onlyId = GetOption(args, "--id");
    var provider = GetOption(args, "--provider") ?? "azure";
    if (provider is not ("azure" or "nvidia"))
    {
        throw new ArgumentException("--provider 仅支持 azure 或 nvidia。");
    }

    var useAzure = provider == "azure";
    var region = GetOption(args, "--region")
        ?? Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION")
        ?? "eastus";
    var courseRoot = ResolveCourseRoot(GetOption(args, "--content-root") ?? "content/courses/de-DE/a1-foundations");
    var audioRoot = Path.GetFullPath(GetOption(args, "--audio-root")
        ?? Path.Combine(courseRoot, "..", "..", "..", "audio"));
    var options = new TtsOptions
    {
        Provider = provider,
        BaseUrl = GetOption(args, "--base-url") ?? (useAzure
            ? $"https://{region}.tts.speech.microsoft.com/cognitiveservices/v1"
            : "https://integrate.api.nvidia.com/v1/"),
        Model = useAzure ? "azure-ai-speech" : "nvidia/magpie-tts-zeroshot",
        Voice = GetOption(args, "--voice") ?? (useAzure ? "de-DE-KatjaNeural" : "auto"),
        ResponseFormat = GetOption(args, "--format") ?? "mp3"
    };

    if (options.ResponseFormat is not ("mp3" or "wav"))
    {
        throw new ArgumentException("--format 仅支持 mp3 或 wav。");
    }

    if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri))
    {
        throw new ArgumentException("--base-url 不是有效的绝对地址。");
    }

    var content = new JsonContentLoader().Load(courseRoot);
    Console.WriteLine($"课程：{content.Course.Id}；词汇 {content.Vocabulary.Count}；句子 {content.Sentences.Count()}。");

    if (dryRun)
    {
        var preview = new AudioGenerationRunner(content, new UnavailableSynthesizer(), options, audioRoot);
        var previewReport = await preview.RunAsync(onlyId, force, true, CancellationToken.None);
        Console.WriteLine($"预览完成：共 {previewReport.Total} 项，跳过 {previewReport.Skipped}，待生成 {previewReport.Pending}。");
        return 0;
    }

    var keyName = useAzure ? "AZURE_SPEECH_KEY" : "NVIDIA_API_KEY";
    var apiKey = Environment.GetEnvironmentVariable(keyName);
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        throw new InvalidOperationException($"请通过环境变量 {keyName} 提供密钥；不要把密钥写入源码或命令行参数。");
    }

    var handler = new HttpClientHandler { AllowAutoRedirect = false };
    using var httpClient = new HttpClient(handler) { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(120) };
    httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("BeafSoup.AudioGenerator/1.0");
    IAudioSynthesizer client;
    if (useAzure)
    {
        client = new AzureTtsClient(httpClient, options, apiKey);
    }
    else
    {
        httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        client = new NvidiaTtsClient(httpClient, options);
    }

    var runner = new AudioGenerationRunner(content, client, options, audioRoot);
    var report = await runner.RunAsync(onlyId, force, false, CancellationToken.None);
    Console.WriteLine($"生成结束：共 {report.Total} 项，新生成 {report.Generated}，跳过 {report.Skipped}，失败 {report.Failed}。");
    return report.Failed == 0 ? 0 : 2;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"音频生成工具已停止：{exception.Message}");
    return 1;
}

static string? GetOption(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static string ResolveCourseRoot(string configuredPath)
{
    if (Path.IsPathRooted(configuredPath) && Directory.Exists(configuredPath))
    {
        return Path.GetFullPath(configuredPath);
    }

    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (directory is not null)
    {
        var candidate = Path.Combine(directory.FullName, configuredPath);
        if (Directory.Exists(candidate))
        {
            return Path.GetFullPath(candidate);
        }

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException($"找不到课程目录：{configuredPath}");
}

static void PrintHelp()
{
    Console.WriteLine("""
        BeafSoup 音频生成工具（Azure AI Speech / NVIDIA Magpie TTS）

        用法：
          dotnet run --project src/BeafSoup.AudioGenerator -- --dry-run
          dotnet run --project src/BeafSoup.AudioGenerator -- --provider azure --region eastus --id v-hallo
          dotnet run --project src/BeafSoup.AudioGenerator -- --provider azure --region eastus
          dotnet run --project src/BeafSoup.AudioGenerator -- --force

        选项：
          --content-root <路径>  课程 JSON 目录
          --audio-root <路径>    音频输出目录，默认 content/audio
          --provider azure|nvidia  语音服务，默认 azure
          --id <资源ID>          只生成一个词汇或句子
          --format mp3|wav      请求格式，默认 mp3
          --region <区域>        Azure 区域，默认 eastus
          --voice <语音>         语音名称，Azure 默认 de-DE-KatjaNeural
          --base-url <地址>      自定义合成接口地址
          --dry-run             只列出任务，不请求服务
          --force               忽略有效缓存并重新生成

        Azure 密钥仅通过 AZURE_SPEECH_KEY 环境变量读取，区域也可通过 AZURE_SPEECH_REGION 设置。
        NVIDIA 密钥仅通过 NVIDIA_API_KEY 环境变量读取。
        """);
}

internal sealed class UnavailableSynthesizer : IAudioSynthesizer
{
    public Task<AudioData> SynthesizeAsync(AudioSynthesisRequest request, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("预览模式不会请求语音服务。");
}
