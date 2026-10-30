namespace BeafSoup.Content;

public sealed class DialogueDocument
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string CourseId { get; set; } = "";
    public int Order { get; set; }
    public string Language { get; set; } = "de-DE";
    public List<string> LessonIds { get; set; } = [];
    public Dictionary<string, string> Title { get; set; } = [];
    public Dictionary<string, string> Scenario { get; set; } = [];
    public Dictionary<string, string> Goal { get; set; } = [];
    public List<DialogueSpeaker> Speakers { get; set; } = [];
    public List<DialogueTurn> Turns { get; set; } = [];
}

public sealed class DialogueSpeaker
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public Dictionary<string, string> Role { get; set; } = [];
    public string Voice { get; set; } = "";
    public string? Style { get; set; }
    public string Rate { get; set; } = "1.0";
    public string Color { get; set; } = "teal";
}

public sealed class DialogueTurn
{
    public string Id { get; set; } = "";
    public string SpeakerId { get; set; } = "";
    public string Text { get; set; } = "";
    public Dictionary<string, string> Translations { get; set; } = [];
    public Dictionary<string, string> Notes { get; set; } = [];
    public List<SentenceSegment> Segments { get; set; } = [];
    public int PauseAfterMs { get; set; }
}

public static class DialogueTiming
{
    public const int MinimumPauseMs = 3000;
    public const int MaximumPauseMs = 12000;

    /// <summary>Reserved shadowing time for a line when content does not prescribe one.
    /// Azure German neural voices speak roughly 120 ms per character at rate 1.0, so
    /// 180 ms per character leaves about 1.5x the spoken duration to repeat the line.</summary>
    public static int DefaultPauseMs(string text) =>
        Math.Clamp((int)Math.Round(text.Length * 180 / 100.0) * 100, MinimumPauseMs, MaximumPauseMs);
}
