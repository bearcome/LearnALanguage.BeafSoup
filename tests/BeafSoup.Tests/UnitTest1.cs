using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BeafSoup.Api;
using BeafSoup.AudioGenerator;
using BeafSoup.Content;
using BeafSoup.Contracts;
using BeafSoup.Data;
using BeafSoup.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;

namespace BeafSoup.Tests;

public sealed class CourseContentTests
{
    [Fact]
    public void InitialCourse_HasCompleteSixLessonPackage()
    {
        var content = TestContent.Load();

        Assert.Equal(6, content.Lessons.Count);
        Assert.Equal(60, content.Vocabulary.Count);
        Assert.Equal(30, content.Sentences.Count());
        Assert.Equal(60, content.Questions.Count);
        Assert.All(content.Lessons, lesson =>
        {
            Assert.Equal(10, lesson.VocabularyIds.Count);
            Assert.Equal(5, lesson.Sentences.Count);
            Assert.Equal(10, lesson.QuestionIds.Count);
        });
    }

    [Fact]
    public void AudioJobs_ContainEveryVocabularyEntrySentenceAndDialogueTurn()
    {
        var content = TestContent.Load();
        var jobs = content.GetAudioJobs();

        Assert.Equal(114, jobs.Count);
        Assert.Equal(60, jobs.Count(job => job.Kind == "vocabulary"));
        Assert.Equal(30, jobs.Count(job => job.Kind == "sentence"));
        Assert.Equal(24, jobs.Count(job => job.Kind == "dialogue"));
        Assert.Equal(114, jobs.Select(job => job.SourceId).Distinct().Count());
        Assert.All(jobs.Where(job => job.Kind == "dialogue"), job =>
        {
            Assert.False(string.IsNullOrWhiteSpace(job.Voice));
            Assert.False(string.IsNullOrWhiteSpace(job.Rate));
        });
    }

    [Fact]
    public void Dialogues_HaveDistinctSpeakersWithVoicesAndShadowingPauses()
    {
        var content = TestContent.Load();

        Assert.Equal(3, content.Dialogues.Count);
        Assert.All(content.Dialogues, dialogue =>
        {
            Assert.Equal(8, dialogue.Turns.Count);
            Assert.True(dialogue.Speakers.Count >= 2);
            Assert.Equal(dialogue.Speakers.Count, dialogue.Speakers.Select(speaker => speaker.Voice).Distinct().Count());
            Assert.All(dialogue.Turns, turn =>
            {
                Assert.Contains(dialogue.Speakers, speaker => speaker.Id == turn.SpeakerId);
                Assert.InRange(turn.PauseAfterMs, DialogueTiming.MinimumPauseMs, DialogueTiming.MaximumPauseMs);
                Assert.Equal(turn.Text, string.Concat(turn.Segments.Select(segment => segment.Text)));
                Assert.True(turn.Translations.ContainsKey("zh-CN"));
            });
        });

        Assert.Equal(24, content.DialogueTurns.Select(turn => turn.Id).Distinct().Count());
        Assert.Equal(24, content.DialogueTurns.Select(turn => turn.Text).Distinct().Count());
        Assert.All(content.DialogueTurns, turn => Assert.NotNull(content.FindTurn(turn.Id)));
    }

    [Fact]
    public void Validator_RejectsDialogueTurnWithBrokenSegments()
    {
        var content = TestContent.Load();
        content.Dialogues[0].Turns[0].Segments[0].Text = "Falsch";

        var exception = Assert.Throws<InvalidDataException>(() => ContentValidator.Validate(content));
        Assert.Contains("分词拼接结果", exception.Message);
        Assert.Contains("dlg-01-first-meeting", exception.Message);
    }

    [Fact]
    public void PublicQuestionJson_DoesNotExposeAnswerOrExplanation()
    {
        var content = TestContent.Load();
        var audio = new AudioUrlResolver(content, new AudioManifest(), TestContent.AudioRoot);
        var dto = ApiContentMapper.ToDto(content.Questions[0], content, audio);

        var json = JsonSerializer.Serialize(dto);

        Assert.DoesNotContain("correctOptionId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("explanation", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("isCorrect", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(4, dto.Options.Count);
    }

    [Fact]
    public void Validator_RejectsSegmentsThatNoLongerMatchSentence()
    {
        var content = TestContent.Load();
        content.Lessons[0].Sentences[0].Segments[0].Text = "Changed";

        var exception = Assert.Throws<InvalidDataException>(() => ContentValidator.Validate(content));
        Assert.Contains("分词拼接结果", exception.Message);
    }

    [Fact]
    public void AudioFingerprint_ChangesWithTextAndSettings()
    {
        Assert.Equal(AudioFingerprint.Hash("Hallo"), AudioFingerprint.Hash("Hallo"));
        Assert.NotEqual(AudioFingerprint.Hash("Hallo"), AudioFingerprint.Hash("Hallo."));
        Assert.NotEqual(
            AudioFingerprint.SettingsHash("model", "auto", "mp3", "de-DE"),
            AudioFingerprint.SettingsHash("model", "male", "mp3", "de-DE"));
    }
}

public sealed class NvidiaClientTests
{
    [Fact]
    public async Task SpeechRequest_UsesMagpieAutoAndReadsAudio()
    {
        var responseMessage = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(TestAudio.Mp3())
        };
        responseMessage.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        var handler = new RecordingHandler(_ => responseMessage);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://integrate.api.nvidia.com/v1/") };
        var client = new NvidiaTtsClient(httpClient, new TtsOptions());

        var result = await client.SynthesizeAsync(new AudioSynthesisRequest("Hallo.", "auto", null, "1.0"), CancellationToken.None);

        Assert.Equal(".mp3", result.Extension);
        Assert.Equal("/v1/audio/speech", handler.RequestUri!.AbsolutePath);
        using var document = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("nvidia/magpie-tts-zeroshot", document.RootElement.GetProperty("model").GetString());
        Assert.Equal("auto", document.RootElement.GetProperty("voice").GetString());
        Assert.Equal("Hallo.", document.RootElement.GetProperty("input").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    public async Task SpeechError_ClassifiesRetryBehavior(HttpStatusCode status, bool transient)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent("provider error")
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://integrate.api.nvidia.com/v1/") };
        var client = new NvidiaTtsClient(httpClient, new TtsOptions());

        var exception = await Assert.ThrowsAsync<TtsException>(() =>
            client.SynthesizeAsync(new AudioSynthesisRequest("Hallo", "auto", null, "1.0"), CancellationToken.None));
        Assert.Equal(transient, exception.Transient);
    }

    [Fact]
    public void AudioValidator_RejectsTextMasqueradingAsAudio()
    {
        Assert.Throws<TtsException>(() => AudioValidator.DetectExtension(
            Encoding.UTF8.GetBytes("404 page not found, this is not an audio file"), "text/plain"));
    }
}

public sealed class AzureClientTests
{
    [Fact]
    public async Task SpeechRequest_UsesAzureHeadersAndEscapesSsml()
    {
        var responseMessage = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(TestAudio.Mp3())
        };
        responseMessage.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        var handler = new RecordingHandler(_ => responseMessage);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://eastus.tts.speech.microsoft.com/cognitiveservices/v1")
        };
        var options = new TtsOptions
        {
            Provider = "azure",
            Model = "azure-ai-speech",
            Voice = "de-DE-KatjaNeural"
        };
        var client = new AzureTtsClient(httpClient, options, "test-key");

        var result = await client.SynthesizeAsync(new AudioSynthesisRequest("Hallo & <Welt>", "de-DE-KatjaNeural", null, "1.0"), CancellationToken.None);

        Assert.Equal(".mp3", result.Extension);
        Assert.Equal("/cognitiveservices/v1", handler.RequestUri!.AbsolutePath);
        Assert.Equal("test-key", handler.RequestHeader("Ocp-Apim-Subscription-Key"));
        Assert.Equal("audio-16khz-32kbitrate-mono-mp3", handler.RequestHeader("X-Microsoft-OutputFormat"));
        Assert.Contains("name=\"de-DE-KatjaNeural\"", handler.RequestBody);
        Assert.Contains("Hallo &amp; &lt;Welt&gt;", handler.RequestBody);
        Assert.Contains("rate=\"1.0\"", handler.RequestBody);
        Assert.DoesNotContain("express-as", handler.RequestBody);
    }

    [Fact]
    public async Task DialogueTurn_UsesSpeakerVoiceStyleAndRate()
    {
        var responseMessage = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(TestAudio.Mp3())
        };
        responseMessage.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        var handler = new RecordingHandler(_ => responseMessage);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://eastus.tts.speech.microsoft.com/cognitiveservices/v1")
        };
        var options = new TtsOptions { Provider = "azure", Model = "azure-ai-speech", Voice = "de-DE-KatjaNeural" };
        var client = new AzureTtsClient(httpClient, options, "test-key");

        var content = TestContent.Load();
        var job = content.GetAudioJobs().First(item => item.Kind == "dialogue" && item.Style is not null);
        await client.SynthesizeAsync(new AudioSynthesisRequest(job.Text, job.Voice!, job.Style, job.Rate!), CancellationToken.None);

        Assert.Contains($"name=\"{job.Voice}\"", handler.RequestBody);
        Assert.Contains($"<mstts:express-as style=\"{job.Style}\"", handler.RequestBody);
        Assert.Contains("https://www.w3.org/2001/mstts", handler.RequestBody);
    }
}

public sealed class AudioRunnerTests
{
    [Fact]
    public async Task Runner_GeneratesPersistsAndResumesWithoutAnotherRequest()
    {
        var content = TestContent.Load();
        var directory = Directory.CreateTempSubdirectory("beafsoup-audio-test-");
        var synthesizer = new CountingSynthesizer();
        var options = new TtsOptions { MaxAttempts = 1 };
        try
        {
            var runner = new AudioGenerationRunner(content, synthesizer, options, directory.FullName);
            var first = await runner.RunAsync("v-hallo", false, false, CancellationToken.None);
            var second = await runner.RunAsync("v-hallo", false, false, CancellationToken.None);

            Assert.Equal(1, first.Generated);
            Assert.Equal(1, second.Skipped);
            Assert.Equal(1, synthesizer.Calls);
            var manifest = new JsonContentLoader().LoadAudioManifest(
                Path.Combine(directory.FullName, "de-DE", "manifest.json"));
            var entry = Assert.Single(manifest.Entries);
            Assert.Equal("ready", entry.Status);
            Assert.Equal(AudioFingerprint.Hash("Hallo"), entry.SourceTextSha256);
            Assert.True(File.Exists(Path.Combine(directory.FullName, "de-DE", entry.RelativePath)));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public async Task Runner_UsesEachSpeakerVoiceAndRegeneratesWhenOnlyTheVoiceChanges()
    {
        var content = TestContent.Load();
        var directory = Directory.CreateTempSubdirectory("beafsoup-dialogue-audio-");
        var synthesizer = new CountingSynthesizer();
        try
        {
            var turn = content.DialogueTurns.First();
            var runner = new AudioGenerationRunner(content, synthesizer, new TtsOptions { MaxAttempts = 1 }, directory.FullName);
            var first = await runner.RunAsync(turn.Id, false, false, CancellationToken.None);
            Assert.Equal(1, first.Generated);
            Assert.Equal("de-DE-Mia:MAI-Voice-2.1", synthesizer.LastRequest!.Voice);
            Assert.Equal("happy", synthesizer.LastRequest.Style);

            // Same text, different voice: the per-entry settings hash must force a rebuild.
            content.Dialogues[0].Speakers[0].Voice = "de-DE-KatjaNeural";
            var changed = await runner.RunAsync(turn.Id, false, false, CancellationToken.None);
            Assert.Equal(1, changed.Generated);
            Assert.Equal("de-DE-KatjaNeural", synthesizer.LastRequest!.Voice);

            var manifest = new JsonContentLoader().LoadAudioManifest(
                Path.Combine(directory.FullName, "de-DE", "manifest.json"));
            Assert.Equal("dialogue", Assert.Single(manifest.Entries).Kind);
        }
        finally
        {
            directory.Delete(true);
        }
    }
}

public sealed class ContentValidatorTests
{
    [Fact]
    public void Validator_RejectsDuplicateLessonOrder()
    {
        var content = TestContent.Load();
        content.Lessons[1].Order = content.Lessons[0].Order;

        var exception = Assert.Throws<InvalidDataException>(() => ContentValidator.Validate(content));
        Assert.Contains("课时顺序", exception.Message);
    }

    [Fact]
    public void Localize_DoesNotUseGermanTextAsChineseTranslation()
    {
        var values = new Dictionary<string, string> { ["de-DE"] = "Hallo" };

        Assert.Equal("", ContentSnapshot.Localize(values, "zh-CN"));
        Assert.Equal("Hallo", ContentSnapshot.Localize(values, "de-DE"));
        Assert.Equal("Hallo", ContentSnapshot.Localize(values, "de-CH"));
    }

    [Fact]
    public void AudioUrlResolver_ReturnsNullWhenFingerprintOrFileDoesNotMatch()
    {
        var directory = Directory.CreateTempSubdirectory("beafsoup-url-test-");
        try
        {
            var content = TestContent.Load();
            var vocabulary = content.Vocabulary[0];
            var job = new AudioSourceItem(vocabulary.Id, "vocabulary", vocabulary.Lemma);
            var relativePath = $"vocabulary/{vocabulary.Id}.mp3";
            var fullPath = Path.Combine(directory.FullName, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllBytes(fullPath, TestAudio.Mp3());
            var manifest = new AudioManifest
            {
                Entries =
                [
                    new AudioManifestEntry
                    {
                        SourceId = job.SourceId,
                        Kind = job.Kind,
                        RelativePath = relativePath,
                        SourceTextSha256 = AudioFingerprint.Hash(job.Text),
                        ByteLength = new FileInfo(fullPath).Length,
                        Status = "ready"
                    }
                ]
            };
            var resolver = new AudioUrlResolver(content, manifest, directory.FullName);

            Assert.Equal($"/audio/de-DE/{relativePath}", resolver.Resolve(job));
            Assert.Null(resolver.Resolve(new AudioSourceItem(job.SourceId, job.Kind, "Anderer Text")));
            manifest.Entries[0].Status = "failed";
            Assert.Null(resolver.Resolve(job));
            manifest.Entries[0].Status = "ready";
            File.Delete(fullPath);
            Assert.Null(resolver.Resolve(job));
        }
        finally
        {
            directory.Delete(true);
        }
    }
}

public sealed class ApiEndpointTests
{
    [Fact]
    public void CourseAndLessonRoutes_ReturnPublicDataAndNotFound()
    {
        using var factory = TestApi.Create();
        var client = factory.CreateClient();

        var health = client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/v1/health").Result;
        Assert.Equal("ok", health.GetProperty("status").GetString());
        Assert.Equal(6, health.GetProperty("lessons").GetInt32());
        Assert.Equal(60, health.GetProperty("questions").GetInt32());

        var summary = client.GetFromJsonAsync<CourseSummaryDto>("/api/v1/courses").Result;
        Assert.Equal("de-a0-a1-foundations", summary!.Id);
        Assert.Equal(6, summary.LessonCount);
        Assert.Equal(60, summary.QuestionCount);

        var lesson = client.GetFromJsonAsync<LessonDetailDto>("/api/v1/lessons/de-l01-greetings").Result;
        Assert.Equal(10, lesson!.Vocabulary.Count);
        Assert.Equal(5, lesson.Sentences.Count);

        var questions = client.GetFromJsonAsync<List<QuestionDto>>("/api/v1/lessons/de-l01-greetings/questions").Result;
        Assert.Equal(10, questions!.Count);
        Assert.All(questions, question => Assert.Equal(4, question.Options.Count));

        Assert.Equal(HttpStatusCode.NotFound, client.GetAsync("/api/v1/lessons/de-l99-missing").Result.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, client.GetAsync("/api/v1/courses/unknown").Result.StatusCode);
    }

    [Fact]
    public void PublicQuestionResponses_NeverContainAnswerOrExplanation()
    {
        using var factory = TestApi.Create();
        var client = factory.CreateClient();

        foreach (var url in new[]
                 {
                     "/api/v1/courses/de-a0-a1-foundations/questions",
                     "/api/v1/lessons/de-l03-numbers-time/questions"
                 })
        {
            var json = client.GetStringAsync(url).Result;
            Assert.DoesNotContain("correctOptionId", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("explanation", json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void GradeEndpoint_ScoresCorrectAndRejectsInvalidInput()
    {
        using var factory = TestApi.Create();
        var client = factory.CreateClient();
        var content = TestContent.Load();

        foreach (var question in content.Questions)
        {
            var correct = client.PostAsJsonAsync($"/api/v1/questions/{question.Id}/grade",
                new GradeRequest(question.CorrectOptionId)).Result.Content.ReadFromJsonAsync<GradeResultDto>().Result!;
            Assert.True(correct.IsCorrect);
            Assert.Equal(10, correct.Score);
            Assert.Equal(question.CorrectOptionId, correct.CorrectOptionId);
            Assert.False(string.IsNullOrWhiteSpace(correct.Explanation));

            var wrongOption = question.Options.First(option => option.Id != question.CorrectOptionId);
            var wrong = client.PostAsJsonAsync($"/api/v1/questions/{question.Id}/grade",
                new GradeRequest(wrongOption.Id)).Result.Content.ReadFromJsonAsync<GradeResultDto>().Result!;
            Assert.False(wrong.IsCorrect);
            Assert.Equal(0, wrong.Score);
        }

        Assert.Equal(HttpStatusCode.BadRequest, client.PostAsJsonAsync("/api/v1/questions/q-l01-01/grade",
            new GradeRequest("z")).Result.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, client.PostAsJsonAsync("/api/v1/questions/q-l01-01/grade",
            new GradeRequest(new string('a', 17))).Result.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, client.PostAsJsonAsync("/api/v1/questions/q-l99-01/grade",
            new GradeRequest("a")).Result.StatusCode);
    }

    [Fact]
    public void EveryAudioUrlResolvesToAnExistingFile()
    {
        using var factory = TestApi.Create();
        var client = factory.CreateClient();
        var content = TestContent.Load();
        var listed = 0;

        foreach (var lesson in content.Lessons)
        {
            var detail = client.GetFromJsonAsync<LessonDetailDto>($"/api/v1/lessons/{lesson.Id}").Result!;
            foreach (var url in detail.Vocabulary.Select(word => word.AudioUrl).Concat(detail.Sentences.Select(sentence => sentence.AudioUrl)))
            {
                Assert.NotNull(url);
                Assert.Equal(HttpStatusCode.OK, client.GetAsync(url).Result.StatusCode);
                listed++;
            }
        }

        Assert.Equal(90, listed);
    }
}

public sealed class DialogueApiTests
{
    [Fact]
    public void DialogueRoutes_ReturnSpeakersTurnsShadowingTimeAndAudio()
    {
        using var factory = TestApi.Create();
        var client = factory.CreateClient();

        var summaries = client.GetFromJsonAsync<List<DialogueSummaryDto>>("/api/v1/dialogues").Result!;
        Assert.Equal(3, summaries.Count);
        Assert.All(summaries, summary => Assert.Equal(8, summary.TurnCount));
        Assert.Equal(summaries.Count, summaries.Select(summary => summary.Id).Distinct().Count());
        Assert.All(summaries, summary => Assert.True(summary.Speakers.Count >= 2));

        var detail = client.GetFromJsonAsync<DialogueDetailDto>("/api/v1/dialogues/dlg-01-first-meeting").Result!;
        Assert.Equal(2, detail.Speakers.Count);
        Assert.Equal(8, detail.Turns.Count);
        Assert.Equal(2, detail.Speakers.Select(speaker => speaker.Id).Distinct().Count());
        Assert.NotEmpty(detail.Vocabulary);
        Assert.All(detail.Turns, turn =>
        {
            Assert.False(string.IsNullOrWhiteSpace(turn.Text));
            Assert.False(string.IsNullOrWhiteSpace(turn.Translation));
            Assert.InRange(turn.PauseAfterMs, DialogueTiming.MinimumPauseMs, DialogueTiming.MaximumPauseMs);
            Assert.Equal(turn.Text, string.Concat(turn.Segments.Select(segment => segment.Text)));
            Assert.NotNull(turn.AudioUrl);
            Assert.Equal(HttpStatusCode.OK, client.GetAsync(turn.AudioUrl).Result.StatusCode);
        });

        var course = client.GetFromJsonAsync<CourseDetailDto>("/api/v1/courses/de-a0-a1-foundations").Result!;
        Assert.Equal(3, course.Dialogues.Count);

        Assert.Equal(HttpStatusCode.NotFound, client.GetAsync("/api/v1/dialogues/dlg-99-missing").Result.StatusCode);
    }
}

public sealed class ProgressApiTests
{
    [Fact]
    public void ProgressSync_MergesRepeatedSubmissionsAndSurvivesNewGuestIds()
    {
        using var factory = TestApi.Create();
        var client = factory.CreateClient();
        var guestId = Guid.NewGuid().ToString();
        var attempt = new AttemptRecordDto(Guid.NewGuid(), "q-l01-01", "a", true, DateTimeOffset.UtcNow);
        var request = new ProgressSyncRequest(guestId, "de-a0-a1-foundations", [attempt], ["de-l01-greetings"]);

        var first = client.PostAsJsonAsync("/api/v1/progress/sync", request).Result;
        first.EnsureSuccessStatusCode();
        var firstBody = first.Content.ReadFromJsonAsync<GuestProgressDto>().Result!;
        Assert.Equal(guestId, firstBody.GuestId);
        Assert.Single(firstBody.Attempts);
        Assert.Single(firstBody.CompletedLessons);

        // Replaying the same payload (offline retry) must not duplicate anything.
        var second = client.PostAsJsonAsync("/api/v1/progress/sync", request).Result.Content
            .ReadFromJsonAsync<GuestProgressDto>().Result!;
        Assert.Single(second.Attempts);
        Assert.Single(second.CompletedLessons);

        // The server is the merge authority: server data plus a new local attempt.
        var extra = new AttemptRecordDto(Guid.NewGuid(), "q-l01-02", "b", false, DateTimeOffset.UtcNow);
        var merged = client.PostAsJsonAsync("/api/v1/progress/sync",
                new ProgressSyncRequest(guestId, "de-a0-a1-foundations", [extra], ["de-l01-greetings", "de-l02-personal-information"]))
            .Result.Content.ReadFromJsonAsync<GuestProgressDto>().Result!;
        Assert.Equal(3, merged.Attempts.Count);
        Assert.Equal(2, merged.CompletedLessons.Count);

        var fetched = client.GetFromJsonAsync<GuestProgressDto>($"/api/v1/progress/{guestId}").Result!;
        Assert.Equal(3, fetched.Attempts.Count);

        Assert.Equal(HttpStatusCode.BadRequest, client.GetAsync("/api/v1/progress/not-a-guid").Result.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, client.GetAsync($"/api/v1/progress/{Guid.NewGuid()}").Result.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            client.PostAsJsonAsync("/api/v1/progress/sync", request with { CourseId = "unknown" }).Result.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            client.PostAsJsonAsync("/api/v1/progress/sync", new ProgressSyncRequest(
                guestId, "de-a0-a1-foundations",
                Enumerable.Range(0, 1001).Select(_ => new AttemptRecordDto(Guid.NewGuid(), "q-l01-01", "a", true, DateTimeOffset.UtcNow)).ToList(),
                [])).Result.StatusCode);

        var named = client.PostAsJsonAsync("/api/v1/progress/sync",
                new ProgressSyncRequest(null, "de-a0-a1-foundations", [], []))
            .Result.Content.ReadFromJsonAsync<GuestProgressDto>().Result!;
        Assert.True(Guid.TryParse(named.GuestId, out _));
        Assert.NotEqual(guestId, named.GuestId);
    }
}

public sealed class PersistenceTests
{
    [Fact]
    public void ProviderSelection_RecognisesPostgresAndDefaultsToSqlite()
    {
        var postgres = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Provider"] = "PostgreSQL" })
            .Build();
        var empty = new ConfigurationBuilder().Build();

        Assert.Equal(PersistenceRegistration.PostgresProvider, PersistenceRegistration.ResolveProvider(postgres));
        Assert.Equal(PersistenceRegistration.SqliteProvider, PersistenceRegistration.ResolveProvider(empty));

        // Both providers must be configurable without a live server.
        var sqliteOptions = PersistenceRegistration.Configure(new DbContextOptionsBuilder(), PersistenceRegistration.SqliteProvider, "Data Source=:memory:");
        Assert.Contains("Sqlite", sqliteOptions.Options.Extensions.First().GetType().FullName);
        var postgresOptions = PersistenceRegistration.Configure(new DbContextOptionsBuilder(), PersistenceRegistration.PostgresProvider, "Host=localhost;Database=beafsoup;Username=x;Password=y");
        Assert.Contains("Npgsql", postgresOptions.Options.Extensions.First().GetType().FullName);
    }

    [Fact]
    public async Task Repository_KeepsAttemptsAndLessonsUniquePerGuest()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"beafsoup-repo-{Guid.NewGuid():N}.db");
        try
        {
            var builder = PersistenceRegistration.Configure(
                new DbContextOptionsBuilder<BeafSoupDbContext>(), PersistenceRegistration.SqliteProvider, $"Data Source={databasePath}");
            await using var db = new BeafSoupDbContext(builder.Options);
            await PersistenceRegistration.InitializeAsync(db);
            var repository = new GuestProgressRepository(db, TimeProvider.System);

            var guestId = Guid.NewGuid().ToString();
            var attempt = new AttemptRecordDto(Guid.NewGuid(), "q-l01-01", "a", true, DateTimeOffset.UtcNow);
            await repository.SyncAsync(new ProgressSyncRequest(guestId, "course", [attempt], ["de-l01-greetings"]), CancellationToken.None);
            await repository.SyncAsync(new ProgressSyncRequest(guestId, "course", [attempt], ["de-l01-greetings"]), CancellationToken.None);

            var stored = await repository.GetAsync(Guid.Parse(guestId), "course", CancellationToken.None);
            Assert.NotNull(stored);
            Assert.Single(stored!.Attempts);
            Assert.Single(stored.CompletedLessons);

            // An attempt id already owned by another guest must not be stolen.
            var other = Guid.NewGuid().ToString();
            var otherStored = await repository.SyncAsync(
                new ProgressSyncRequest(other, "course", [attempt], []), CancellationToken.None);
            Assert.Empty(otherStored.Attempts);

            Assert.Null(await repository.GetAsync(Guid.NewGuid(), "course", CancellationToken.None));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }
}

/// <summary>Starts the API against a private SQLite file so tests never share state,
/// and removes it on dispose.</summary>
internal sealed class TestApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"beafsoup-api-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:BeafSoup", $"Data Source={_databasePath}");
        builder.UseSetting("Database:Provider", PersistenceRegistration.SqliteProvider);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(_databasePath)) File.Delete(_databasePath);
        }
        catch (IOException)
        {
            // The temp file is left for the OS to clean up.
        }
    }
}

internal static class TestApi
{
    public static TestApiFactory Create() => new();
}

public sealed class GuestProgressStoreTests
{
    [Fact]
    public async Task LoadAsync_SurvivesUnavailableStorageAndRestoresMatchingCourse()
    {
        var state = new GuestProgressState
        {
            CourseId = "course-a",
            CompletedLessons = ["de-l01-greetings"],
            Attempts = [new AttemptRecord(new Guid("00000000-0000-0000-0000-000000000001"), "q-l01-01", "a", true, DateTimeOffset.UtcNow)]
        };
        var storage = new FakeStorage(new Dictionary<string, string> { ["beafsoup.guest-progress.v1"] = JsonSerializer.Serialize(state) });
        var store = new GuestProgressStore(storage, OfflineApi());

        var restored = await store.LoadAsync("course-a");
        Assert.Contains("de-l01-greetings", restored.CompletedLessons);
        Assert.Single(restored.Attempts);
        Assert.False(store.SyncedToServer);

        Assert.Empty((await store.LoadAsync("course-b")).CompletedLessons);

        var failing = new GuestProgressStore(new ThrowingStorage(), OfflineApi());
        Assert.Empty((await failing.LoadAsync("course-a")).Attempts);
        Assert.NotNull(failing.StorageWarning);
    }

    [Fact]
    public async Task RecordAttemptAndMarkLessonComplete_PersistEveryAttemptOnce()
    {
        var storage = new FakeStorage([]);
        var store = new GuestProgressStore(storage, OfflineApi());
        var state = await store.LoadAsync("course-a");
        var question = new QuestionDto("q-l01-01", "Prompt", "Instruction",
            [new QuestionOptionDto("a", "A"), new QuestionOptionDto("b", "B"), new QuestionOptionDto("c", "C"), new QuestionOptionDto("d", "D")], null);

        await store.RecordAttemptAsync(state, question, new GradeResultDto("q-l01-01", "a", false, "b", "B", "解析", 0));
        await store.RecordAttemptAsync(state, question, new GradeResultDto("q-l01-01", "a", false, "b", "B", "解析", 0));
        await store.MarkLessonCompleteAsync(state, "de-l01-greetings");
        await store.MarkLessonCompleteAsync(state, "de-l01-greetings");

        var saved = JsonSerializer.Deserialize<GuestProgressState>(storage.Get("beafsoup.guest-progress.v1")!)!;
        Assert.Equal(2, saved.Attempts.Count);
        Assert.Single(saved.CompletedLessons);
        Assert.False(string.IsNullOrWhiteSpace(store.GuestId));
    }

    [Fact]
    public async Task LoadAsync_TakesServerStateAndKeepsLocalAttemptsWhenGoingOffline()
    {
        var localAttempt = new AttemptRecord(Guid.NewGuid(), "q-l01-01", "a", true, DateTimeOffset.UtcNow);
        var storage = new FakeStorage(new Dictionary<string, string>
        {
            ["beafsoup.guest-progress.v1"] = JsonSerializer.Serialize(new GuestProgressState
            {
                CourseId = "course-a",
                Attempts = [localAttempt]
            })
        });
        var serverAttempt = new AttemptRecordDto(
            new Guid("00000000-0000-0000-0000-0000000000ff"), "q-l01-02", "b", false, DateTimeOffset.UtcNow);
        var api = ApiClient(request =>
        {
            var payload = JsonSerializer.Deserialize<ProgressSyncRequest>(
                request.Content!.ReadAsStringAsync().Result,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            var attempts = payload.Attempts.Concat([serverAttempt]).DistinctBy(attempt => attempt.Id).ToList();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new GuestProgressDto(
                    "11111111-1111-1111-1111-111111111111",
                    "course-a",
                    ["de-l01-greetings"],
                    attempts,
                    DateTimeOffset.UtcNow))
            };
        });
        var store = new GuestProgressStore(storage, api);

        var state = await store.LoadAsync("course-a");
        Assert.True(store.SyncedToServer);
        Assert.Equal(2, state.Attempts.Count);
        Assert.Contains(state.Attempts, attempt => attempt.Id == localAttempt.Id);

        state = await store.RecordAttemptAsync(
            state,
            new QuestionDto("q-l01-03", "P", "I", [new QuestionOptionDto("a", "A"), new QuestionOptionDto("b", "B"), new QuestionOptionDto("c", "C"), new QuestionOptionDto("d", "D")], null),
            new GradeResultDto("q-l01-03", "a", true, "a", "A", "解析", 10));

        Assert.Equal("11111111-1111-1111-1111-111111111111", store.GuestId);
        Assert.Equal(3, state.Attempts.Count);
        Assert.Contains(state.Attempts, attempt => attempt.Id == localAttempt.Id);
        Assert.Contains(state.Attempts, attempt => attempt.QuestionId == "q-l01-03");
        Assert.Contains("de-l01-greetings", state.CompletedLessons);
        Assert.Equal("11111111-1111-1111-1111-111111111111", storage.Get("beafsoup.guest-id.v1"));

        // Losing the server must not lose the attempts the learner already made.
        var offline = new GuestProgressStore(storage, OfflineApi());
        var cached = await offline.LoadAsync("course-a");
        Assert.False(offline.SyncedToServer);
        Assert.Null(offline.StorageWarning);
        Assert.Equal(3, cached.Attempts.Count);
    }

    private static LearningApiClient OfflineApi() => ApiClient(_ => throw new HttpRequestException("offline"));

    private static LearningApiClient ApiClient(Func<HttpRequestMessage, HttpResponseMessage> response) =>
        new(new HttpClient(new RecordingHandler(response)) { BaseAddress = new Uri("http://localhost:5049/") });

    private sealed class FakeStorage(Dictionary<string, string> values) : IJSRuntime
    {
        public string? Get(string key) => values.TryGetValue(key, out var value) ? value : null;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "beafStorage.get") return ValueTask.FromResult((TValue)(object?)Get((string)args![0]!)!);
            if (identifier == "beafStorage.set")
            {
                values[(string)args![0]!] = (string)args[1]!;
                return ValueTask.FromResult(default(TValue)!);
            }

            throw new InvalidOperationException($"未知的 JS 调用：{identifier}");
        }
    }

    private sealed class ThrowingStorage : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromException<TValue>(new JSException("blocked"));

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            ValueTask.FromException<TValue>(new JSException("blocked"));
    }
}

internal static class TestContent
{
    public static string CourseRoot { get; } = Find("content/courses/de-DE/a1-foundations");
    public static string AudioRoot { get; } = Find("content/audio");
    public static ContentSnapshot Load() => new JsonContentLoader().Load(CourseRoot);

    private static string Find(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relative);
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException(relative);
    }
}

internal static class TestAudio
{
    public static byte[] Mp3()
    {
        var bytes = new byte[128];
        bytes[0] = (byte)'I';
        bytes[1] = (byte)'D';
        bytes[2] = (byte)'3';
        bytes[3] = 4;
        return bytes;
    }
}

internal sealed class CountingSynthesizer : IAudioSynthesizer
{
    public int Calls { get; private set; }
    public AudioSynthesisRequest? LastRequest { get; private set; }

    public Task<AudioData> SynthesizeAsync(AudioSynthesisRequest request, CancellationToken cancellationToken)
    {
        Calls++;
        LastRequest = request;
        return Task.FromResult(new AudioData(TestAudio.Mp3(), "audio/mpeg", ".mp3"));
    }
}

internal sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
{
    public string? RequestBody { get; private set; }
    public Uri? RequestUri { get; private set; }
    public Dictionary<string, string[]> Headers { get; private set; } = [];

    public string? RequestHeader(string name) =>
        Headers.TryGetValue(name, out var values) ? values.SingleOrDefault() : null;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestUri = request.RequestUri;
        RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Headers = request.Headers.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        return response(request);
    }
}
