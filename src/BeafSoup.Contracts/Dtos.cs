namespace BeafSoup.Contracts;

public sealed record CourseSummaryDto(
    string Id,
    string Title,
    string Description,
    string Level,
    string TargetLanguage,
    int LessonCount,
    int QuestionCount);

public sealed record CourseDetailDto(
    string Id,
    string Title,
    string Description,
    string Level,
    string TargetLanguage,
    IReadOnlyList<LessonSummaryDto> Lessons,
    IReadOnlyList<DialogueSummaryDto> Dialogues);

public sealed record LessonSummaryDto(
    string Id,
    int Order,
    string Title,
    string Summary,
    int VocabularyCount,
    int SentenceCount,
    int QuestionCount);

public sealed record LessonDetailDto(
    string Id,
    string CourseId,
    int Order,
    string Title,
    string Summary,
    IReadOnlyList<string> Objectives,
    string Grammar,
    IReadOnlyList<VocabularyDto> Vocabulary,
    IReadOnlyList<SentenceDto> Sentences,
    int QuestionCount);

public sealed record VocabularyDto(
    string Id,
    string Lemma,
    string Translation,
    string PartOfSpeech,
    string? Gender,
    string? Plural,
    string? Note,
    IReadOnlyList<WordFormDto> Forms,
    string? AudioUrl);

public sealed record WordFormDto(string Form, string Note);

public sealed record SentenceDto(
    string Id,
    string Text,
    string Translation,
    string? Note,
    string? AudioUrl,
    IReadOnlyList<SentenceSegmentDto> Segments);

public sealed record SentenceSegmentDto(
    string Text,
    string? VocabularyId,
    string? Note,
    IReadOnlyDictionary<string, string> Morphology);

public sealed record QuestionDto(
    string Id,
    string Prompt,
    string Instruction,
    IReadOnlyList<QuestionOptionDto> Options,
    string? AudioUrl);

public sealed record QuestionOptionDto(string Id, string Text);

public sealed record GradeRequest(string SelectedOptionId);

public sealed record GradeResultDto(
    string QuestionId,
    string SelectedOptionId,
    bool IsCorrect,
    string CorrectOptionId,
    string CorrectOptionText,
    string Explanation,
    int Score);
