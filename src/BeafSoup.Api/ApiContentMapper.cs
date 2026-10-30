using BeafSoup.Content;
using BeafSoup.Contracts;

namespace BeafSoup.Api;

public static class ApiContentMapper
{
    public static CourseSummaryDto ToSummary(ContentSnapshot content) => new(
        content.Course.Id,
        Text(content.Course.Title, content),
        Text(content.Course.Description, content),
        content.Course.Level,
        content.Course.TargetLanguage,
        content.Lessons.Count,
        content.Questions.Count);

    public static CourseDetailDto ToDetail(ContentSnapshot content) => new(
        content.Course.Id,
        Text(content.Course.Title, content),
        Text(content.Course.Description, content),
        content.Course.Level,
        content.Course.TargetLanguage,
        content.Lessons.Select(lesson => ToSummary(lesson, content)).ToList(),
        DialogueMapper.ToSummaries(content));

    public static LessonDetailDto ToDetail(
        LessonDocument lesson,
        ContentSnapshot content,
        AudioUrlResolver audio) => new(
        lesson.Id,
        lesson.CourseId,
        lesson.Order,
        Text(lesson.Title, content),
        Text(lesson.Summary, content),
        lesson.Objectives.GetValueOrDefault(content.Course.DefaultAssistanceLanguage) ?? [],
        Text(lesson.Grammar, content),
        lesson.VocabularyIds.Select(id => ToDto(content.VocabularyById[id], content, audio)).ToList(),
        lesson.Sentences.Select(sentence => ToDto(sentence, content, audio)).ToList(),
        lesson.QuestionIds.Count);

    public static QuestionDto ToDto(
        QuestionDocument question,
        ContentSnapshot content,
        AudioUrlResolver audio)
    {
        string? audioUrl = null;
        if (question.RelatedSentenceId is { } sentenceId &&
            content.FindSentence(sentenceId) is { } sentence)
        {
            audioUrl = audio.Resolve(new AudioSourceItem(sentence.Id, "sentence", sentence.Text));
        }

        return new QuestionDto(
            question.Id,
            question.Prompt[question.PromptLanguage],
            Text(question.Instruction, content),
            question.Options.Select(option => new QuestionOptionDto(option.Id, option.Text)).ToList(),
            audioUrl);
    }

    private static LessonSummaryDto ToSummary(LessonDocument lesson, ContentSnapshot content) => new(
        lesson.Id,
        lesson.Order,
        Text(lesson.Title, content),
        Text(lesson.Summary, content),
        lesson.VocabularyIds.Count,
        lesson.Sentences.Count,
        lesson.QuestionIds.Count);

    public static VocabularyDto ToDto(
        VocabularyEntry entry,
        ContentSnapshot content,
        AudioUrlResolver audio) => new(
        entry.Id,
        entry.Lemma,
        Text(entry.Translations, content),
        entry.PartOfSpeech,
        entry.Gender,
        entry.Plural,
        entry.Notes.Count == 0 ? null : Text(entry.Notes, content),
        entry.Forms.Select(form => new WordFormDto(form.Form, Text(form.Note, content))).ToList(),
        audio.Resolve(new AudioSourceItem(entry.Id, "vocabulary", entry.Lemma)));

    private static SentenceDto ToDto(
        SentenceDocument sentence,
        ContentSnapshot content,
        AudioUrlResolver audio) => new(
        sentence.Id,
        sentence.Text,
        Text(sentence.Translations, content),
        sentence.Notes.Count == 0 ? null : Text(sentence.Notes, content),
        audio.Resolve(new AudioSourceItem(sentence.Id, "sentence", sentence.Text)),
        sentence.Segments.Select(segment => new SentenceSegmentDto(
            segment.Text,
            segment.VocabularyId,
            segment.Notes.Count == 0 ? null : Text(segment.Notes, content),
            segment.Morphology)).ToList());

    private static string Text(IReadOnlyDictionary<string, string> values, ContentSnapshot content) =>
        ContentSnapshot.Localize(values, content.Course.DefaultAssistanceLanguage);
}
