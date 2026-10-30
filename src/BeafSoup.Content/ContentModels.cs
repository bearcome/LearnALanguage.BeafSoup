namespace BeafSoup.Content;

public sealed class CourseDocument
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public int Revision { get; set; } = 1;
    public string TargetLanguage { get; set; } = "de-DE";
    public string DefaultAssistanceLanguage { get; set; } = "zh-CN";
    public string Level { get; set; } = "A0-A1";
    public Dictionary<string, string> Title { get; set; } = [];
    public Dictionary<string, string> Description { get; set; } = [];
    public List<string> LessonIds { get; set; } = [];
    public List<string> DialogueIds { get; set; } = [];
}

public sealed class LessonDocument
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string CourseId { get; set; } = "";
    public int Order { get; set; }
    public Dictionary<string, string> Title { get; set; } = [];
    public Dictionary<string, string> Summary { get; set; } = [];
    public Dictionary<string, List<string>> Objectives { get; set; } = [];
    public Dictionary<string, string> Grammar { get; set; } = [];
    public List<string> VocabularyIds { get; set; } = [];
    public List<string> QuestionIds { get; set; } = [];
    public List<SentenceDocument> Sentences { get; set; } = [];
}

public sealed class VocabularyEntry
{
    public string Id { get; set; } = "";
    public string Language { get; set; } = "de-DE";
    public string Lemma { get; set; } = "";
    public string PartOfSpeech { get; set; } = "";
    public Dictionary<string, string> Translations { get; set; } = [];
    public string? Gender { get; set; }
    public string? Plural { get; set; }
    public Dictionary<string, string> Notes { get; set; } = [];
    public List<WordForm> Forms { get; set; } = [];
}

public sealed class WordForm
{
    public string Form { get; set; } = "";
    public Dictionary<string, string> Note { get; set; } = [];
}

public sealed class SentenceDocument
{
    public string Id { get; set; } = "";
    public string Language { get; set; } = "de-DE";
    public string Text { get; set; } = "";
    public Dictionary<string, string> Translations { get; set; } = [];
    public Dictionary<string, string> Notes { get; set; } = [];
    public List<SentenceSegment> Segments { get; set; } = [];
}

public sealed class SentenceSegment
{
    public string Text { get; set; } = "";
    public string? VocabularyId { get; set; }
    public Dictionary<string, string> Notes { get; set; } = [];
    public Dictionary<string, string> Morphology { get; set; } = [];
}

public sealed class QuestionDocument
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "singleChoice";
    public string TargetLanguage { get; set; } = "de-DE";
    public string PromptLanguage { get; set; } = "de-DE";
    public Dictionary<string, string> Prompt { get; set; } = [];
    public Dictionary<string, string> Instruction { get; set; } = [];
    public List<QuestionOption> Options { get; set; } = [];
    public string CorrectOptionId { get; set; } = "";
    public Dictionary<string, string> Explanation { get; set; } = [];
    public List<string> RelatedVocabularyIds { get; set; } = [];
    public string? RelatedSentenceId { get; set; }
    public int Difficulty { get; set; } = 1;
}

public sealed class QuestionOption
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
}
