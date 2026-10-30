using BeafSoup.Api;
using BeafSoup.Content;
using BeafSoup.Contracts;
using BeafSoup.Data;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options => options.AddPolicy("LearningClient", policy =>
    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddBeafSoupPersistence(builder.Configuration);

var courseRoot = RepositoryPath.Resolve(builder.Configuration["Content:CourseRoot"]
    ?? "content/courses/de-DE/a1-foundations");
var audioRoot = RepositoryPath.Resolve(builder.Configuration["Content:AudioRoot"]
    ?? "content/audio");
var loader = new JsonContentLoader();
var content = loader.Load(courseRoot);
var audioLanguageRoot = Path.Combine(audioRoot, content.Course.TargetLanguage);
var manifest = loader.LoadAudioManifest(Path.Combine(audioLanguageRoot, "manifest.json"));
var audio = new AudioUrlResolver(content, manifest, audioLanguageRoot);

builder.Services.AddSingleton(content);
builder.Services.AddSingleton(audio);
builder.Services.AddSingleton(manifest);

var app = builder.Build();
app.UseCors("LearningClient");

await using (var scope = app.Services.CreateAsyncScope())
{
    await PersistenceRegistration.InitializeAsync(scope.ServiceProvider.GetRequiredService<BeafSoupDbContext>());
}

Directory.CreateDirectory(audioRoot);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(audioRoot),
    RequestPath = "/audio"
});

app.MapGet("/api/v1/health", () => Results.Ok(new
{
    status = "ok",
    courseId = content.Course.Id,
    lessons = content.Lessons.Count,
    dialogues = content.Dialogues.Count,
    dialogueTurns = content.DialogueTurns.Count(),
    vocabulary = content.Vocabulary.Count,
    sentences = content.Sentences.Count(),
    questions = content.Questions.Count,
    audioEntries = manifest.Entries.Count(entry => entry.Status == "ready"),
    storage = PersistenceRegistration.ResolveProvider(app.Configuration)
}));

app.MapGet("/api/v1/courses", () => Results.Ok(ApiContentMapper.ToSummary(content)));

app.MapGet("/api/v1/courses/{courseId}", (string courseId) =>
    courseId == content.Course.Id
        ? Results.Ok(ApiContentMapper.ToDetail(content))
        : Results.NotFound(new { message = "课程不存在。" }));

app.MapGet("/api/v1/lessons/{lessonId}", (string lessonId) =>
    content.LessonsById.TryGetValue(lessonId, out var lesson)
        ? Results.Ok(ApiContentMapper.ToDetail(lesson, content, audio))
        : Results.NotFound(new { message = "课时不存在。" }));

app.MapGet("/api/v1/lessons/{lessonId}/questions", (string lessonId) =>
{
    if (!content.LessonsById.TryGetValue(lessonId, out var lesson))
    {
        return Results.NotFound(new { message = "课时不存在。" });
    }

    var questions = lesson.QuestionIds
        .Select(id => ApiContentMapper.ToDto(content.QuestionsById[id], content, audio))
        .ToList();
    return Results.Ok(questions);
});

app.MapGet("/api/v1/courses/{courseId}/questions", (string courseId, string? lessonId) =>
{
    if (courseId != content.Course.Id)
    {
        return Results.NotFound(new { message = "课程不存在。" });
    }

    IEnumerable<QuestionDocument> questions = content.Questions;
    if (lessonId is not null)
    {
        if (!content.LessonsById.TryGetValue(lessonId, out var lesson))
        {
            return Results.NotFound(new { message = "课时不存在。" });
        }

        questions = lesson.QuestionIds.Select(id => content.QuestionsById[id]);
    }

    return Results.Ok(questions.Select(question => ApiContentMapper.ToDto(question, content, audio)).ToList());
});

app.MapPost("/api/v1/questions/{questionId}/grade", (string questionId, GradeRequest? request) =>
{
    if (!content.QuestionsById.TryGetValue(questionId, out var question))
    {
        return Results.NotFound(new { message = "题目不存在。" });
    }

    var selectedOptionId = request?.SelectedOptionId;
    if (string.IsNullOrWhiteSpace(selectedOptionId) || selectedOptionId.Length > 16)
    {
        return Results.BadRequest(new { message = "请选择一个有效选项。" });
    }

    var selected = question.Options.FirstOrDefault(option => option.Id == selectedOptionId);
    if (selected is null)
    {
        return Results.BadRequest(new { message = "所选选项不属于这道题。" });
    }

    var correct = question.Options.Single(option => option.Id == question.CorrectOptionId);
    var isCorrect = selected.Id == correct.Id;
    return Results.Ok(new GradeResultDto(
        question.Id,
        selected.Id,
        isCorrect,
        correct.Id,
        correct.Text,
        ContentSnapshot.Localize(question.Explanation, content.Course.DefaultAssistanceLanguage),
        isCorrect ? 10 : 0));
});

app.MapGet("/api/v1/dialogues", () => Results.Ok(DialogueMapper.ToSummaries(content)));

app.MapGet("/api/v1/dialogues/{dialogueId}", (string dialogueId) =>
    content.DialoguesById.TryGetValue(dialogueId, out var dialogue)
        ? Results.Ok(DialogueMapper.ToDetail(dialogue, content, audio))
        : Results.NotFound(new { message = "对话不存在。" }));

app.MapGet("/api/v1/progress/{guestId}", async (
    string guestId,
    string? courseId,
    IGuestProgressRepository repository,
    CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(guestId, out var parsed))
    {
        return Results.BadRequest(new { message = "游客标识无效。" });
    }

    var progress = await repository.GetAsync(parsed, courseId ?? content.Course.Id, cancellationToken);
    return progress is null
        ? Results.NotFound(new { message = "没有找到该游客的进度。" })
        : Results.Ok(progress);
});

app.MapPost("/api/v1/progress/sync", async (
    ProgressSyncRequest request,
    IGuestProgressRepository repository,
    CancellationToken cancellationToken) =>
{
    if (request.CourseId != content.Course.Id)
    {
        return Results.NotFound(new { message = "课程不存在。" });
    }

    var attempts = request.Attempts ?? [];
    if (attempts.Count > 1000)
    {
        return Results.BadRequest(new { message = "一次最多同步 1000 条作答记录。" });
    }

    if (attempts.Any(attempt =>
            attempt.QuestionId.Length > 64 || attempt.SelectedOptionId.Length > 64 ||
            string.IsNullOrWhiteSpace(attempt.QuestionId) || string.IsNullOrWhiteSpace(attempt.SelectedOptionId)))
    {
        return Results.BadRequest(new { message = "作答记录字段不合法。" });
    }

    return Results.Ok(await repository.SyncAsync(request, cancellationToken));
});

app.Run();

public partial class Program;
