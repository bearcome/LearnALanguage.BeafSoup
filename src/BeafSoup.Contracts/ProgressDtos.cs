namespace BeafSoup.Contracts;

public sealed record AttemptRecordDto(
    Guid Id,
    string QuestionId,
    string SelectedOptionId,
    bool IsCorrect,
    DateTimeOffset AnsweredAtUtc);

public sealed record GuestProgressDto(
    string GuestId,
    string CourseId,
    IReadOnlyList<string> CompletedLessons,
    IReadOnlyList<AttemptRecordDto> Attempts,
    DateTimeOffset UpdatedAtUtc);

/// <summary>Full-state sync: the server unions attempts by id and completed lessons by id,
/// so repeated submissions after an offline period stay idempotent.</summary>
public sealed record ProgressSyncRequest(
    string? GuestId,
    string CourseId,
    IReadOnlyList<AttemptRecordDto> Attempts,
    IReadOnlyList<string> CompletedLessons);
