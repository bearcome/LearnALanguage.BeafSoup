using System.Text.Json;

namespace BeafSoup.Content;

public sealed class JsonContentLoader
{
    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public ContentSnapshot Load(string courseRoot)
    {
        var root = Path.GetFullPath(courseRoot);
        var course = Read<CourseDocument>(Path.Combine(root, "course.json"));
        var vocabulary = Read<List<VocabularyEntry>>(Path.Combine(root, "vocabulary.json"));
        var questions = Read<List<QuestionDocument>>(Path.Combine(root, "questions.json"));
        var lessons = course.LessonIds
            .Select(id => Read<LessonDocument>(Path.Combine(root, "lessons", $"{id}.json")))
            .ToList();
        var dialogues = course.DialogueIds
            .Select(id => Read<DialogueDocument>(Path.Combine(root, "dialogues", $"{id}.json")))
            .ToList();

        var snapshot = new ContentSnapshot
        {
            Course = course,
            Lessons = lessons,
            Vocabulary = vocabulary,
            Questions = questions,
            Dialogues = dialogues,
            LessonsById = lessons.ToDictionary(item => item.Id),
            VocabularyById = vocabulary.ToDictionary(item => item.Id),
            QuestionsById = questions.ToDictionary(item => item.Id),
            DialoguesById = dialogues.ToDictionary(item => item.Id)
        };

        ContentValidator.Validate(snapshot);
        return snapshot;
    }

    public AudioManifest LoadAudioManifest(string path)
    {
        if (!File.Exists(path))
        {
            return new AudioManifest();
        }

        var manifest = Read<AudioManifest>(path);
        if (manifest.Entries.GroupBy(item => item.SourceId).Any(group => group.Count() > 1))
        {
            throw new InvalidDataException("音频清单中存在重复的资源 ID。");
        }

        return manifest;
    }

    private static T Read<T>(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"缺少课程内容文件：{path}", path);
        }

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, SerializerOptions)
            ?? throw new InvalidDataException($"课程内容文件为空：{path}");
    }
}
