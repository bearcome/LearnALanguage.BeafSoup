namespace BeafSoup.Content;

public sealed class ContentSnapshot
{
    public required CourseDocument Course { get; init; }
    public required IReadOnlyList<LessonDocument> Lessons { get; init; }
    public required IReadOnlyList<VocabularyEntry> Vocabulary { get; init; }
    public required IReadOnlyList<QuestionDocument> Questions { get; init; }
    public IReadOnlyList<DialogueDocument> Dialogues { get; init; } = [];

    public IReadOnlyDictionary<string, LessonDocument> LessonsById { get; init; } =
        new Dictionary<string, LessonDocument>();
    public IReadOnlyDictionary<string, VocabularyEntry> VocabularyById { get; init; } =
        new Dictionary<string, VocabularyEntry>();
    public IReadOnlyDictionary<string, QuestionDocument> QuestionsById { get; init; } =
        new Dictionary<string, QuestionDocument>();
    public IReadOnlyDictionary<string, DialogueDocument> DialoguesById { get; init; } =
        new Dictionary<string, DialogueDocument>();

    public IEnumerable<SentenceDocument> Sentences => Lessons.SelectMany(lesson => lesson.Sentences);

    public IEnumerable<DialogueTurn> DialogueTurns => Dialogues.SelectMany(dialogue => dialogue.Turns);

    public IReadOnlyList<AudioSourceItem> GetAudioJobs()
    {
        var jobs = new List<AudioSourceItem>();
        jobs.AddRange(Vocabulary.Select(item => new AudioSourceItem(item.Id, "vocabulary", item.Lemma)));
        jobs.AddRange(Sentences.Select(item => new AudioSourceItem(item.Id, "sentence", item.Text)));
        foreach (var dialogue in Dialogues)
        {
            var speakersById = dialogue.Speakers.ToDictionary(speaker => speaker.Id);
            jobs.AddRange(dialogue.Turns.Select(turn =>
            {
                var speaker = speakersById[turn.SpeakerId];
                return new AudioSourceItem(turn.Id, "dialogue", turn.Text, speaker.Voice, speaker.Style, speaker.Rate);
            }));
        }

        return jobs;
    }

    public SentenceDocument? FindSentence(string id) =>
        Sentences.FirstOrDefault(sentence => sentence.Id == id);

    public DialogueTurn? FindTurn(string id) =>
        DialogueTurns.FirstOrDefault(turn => turn.Id == id);

    public static string Localize(IReadOnlyDictionary<string, string> values, string language)
    {
        if (values.TryGetValue(language, out var value))
        {
            return value;
        }

        var neutralLanguage = language.Split('-')[0];
        var match = values.FirstOrDefault(pair =>
            pair.Key.Split('-')[0].Equals(neutralLanguage, StringComparison.OrdinalIgnoreCase));
        return match.Value ?? (language.StartsWith("de-", StringComparison.OrdinalIgnoreCase)
            ? values.GetValueOrDefault("de-DE") ?? ""
            : "");
    }
}
