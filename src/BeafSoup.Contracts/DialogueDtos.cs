namespace BeafSoup.Contracts;

public sealed record DialogueSummaryDto(
    string Id,
    int Order,
    string Title,
    string Scenario,
    string Goal,
    int TurnCount,
    IReadOnlyList<string> LessonIds,
    IReadOnlyList<DialogueSpeakerDto> Speakers);

public sealed record DialogueDetailDto(
    string Id,
    int Order,
    string Title,
    string Scenario,
    string Goal,
    IReadOnlyList<string> LessonIds,
    IReadOnlyList<DialogueSpeakerDto> Speakers,
    IReadOnlyList<VocabularyDto> Vocabulary,
    IReadOnlyList<DialogueTurnDto> Turns);

public sealed record DialogueSpeakerDto(
    string Id,
    string Name,
    string Role,
    string Color);

public sealed record DialogueTurnDto(
    string Id,
    string SpeakerId,
    string Text,
    string Translation,
    string? Note,
    string? AudioUrl,
    int PauseAfterMs,
    IReadOnlyList<SentenceSegmentDto> Segments);
