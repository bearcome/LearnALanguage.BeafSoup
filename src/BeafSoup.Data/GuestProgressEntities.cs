namespace BeafSoup.Data;

/// <summary>A guest device profile. Accounts are a later milestone; this keeps a stable id
/// so server-side progress survives browser storage loss.</summary>
public sealed class GuestProfile
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public List<GuestAttempt> Attempts { get; set; } = [];
    public List<GuestCompletedLesson> CompletedLessons { get; set; } = [];
}

public sealed class GuestAttempt
{
    public Guid Id { get; set; }
    public Guid GuestProfileId { get; set; }
    public string QuestionId { get; set; } = "";
    public string SelectedOptionId { get; set; } = "";
    public bool IsCorrect { get; set; }
    public DateTimeOffset AnsweredAtUtc { get; set; }
}

public sealed class GuestCompletedLesson
{
    public Guid Id { get; set; }
    public Guid GuestProfileId { get; set; }
    public string LessonId { get; set; } = "";
    public DateTimeOffset CompletedAtUtc { get; set; }
}
