using BeafSoup.Content;
using BeafSoup.Contracts;

namespace BeafSoup.Api;

public static class DialogueMapper
{
    public static IReadOnlyList<DialogueSummaryDto> ToSummaries(ContentSnapshot content) =>
        content.Dialogues.Select(dialogue => ToSummary(dialogue, content)).ToList();

    public static DialogueSummaryDto ToSummary(DialogueDocument dialogue, ContentSnapshot content) => new(
        dialogue.Id,
        dialogue.Order,
        Text(dialogue.Title, content),
        Text(dialogue.Scenario, content),
        Text(dialogue.Goal, content),
        dialogue.Turns.Count,
        dialogue.LessonIds,
        ToSpeakers(dialogue, content));

    public static DialogueDetailDto ToDetail(DialogueDocument dialogue, ContentSnapshot content, AudioUrlResolver audio) => new(
        dialogue.Id,
        dialogue.Order,
        Text(dialogue.Title, content),
        Text(dialogue.Scenario, content),
        Text(dialogue.Goal, content),
        dialogue.LessonIds,
        ToSpeakers(dialogue, content),
        ReferencedVocabulary(dialogue, content, audio),
        dialogue.Turns.Select(turn => new DialogueTurnDto(
            turn.Id,
            turn.SpeakerId,
            turn.Text,
            Text(turn.Translations, content),
            turn.Notes.Count == 0 ? null : Text(turn.Notes, content),
            audio.Resolve(new AudioSourceItem(turn.Id, "dialogue", turn.Text)),
            turn.PauseAfterMs > 0 ? turn.PauseAfterMs : DialogueTiming.DefaultPauseMs(turn.Text),
            turn.Segments.Select(segment => new SentenceSegmentDto(
                segment.Text,
                segment.VocabularyId,
                segment.Notes.Count == 0 ? null : Text(segment.Notes, content),
                segment.Morphology)).ToList())).ToList());

    private static IReadOnlyList<DialogueSpeakerDto> ToSpeakers(DialogueDocument dialogue, ContentSnapshot content) =>
        dialogue.Speakers.Select(speaker => new DialogueSpeakerDto(
            speaker.Id,
            speaker.Name,
            Text(speaker.Role, content),
            speaker.Color)).ToList();

    /// <summary>Vocabulary entries the dialogue links to, so the player can show word hints
    /// without a second request.</summary>
    private static IReadOnlyList<VocabularyDto> ReferencedVocabulary(
        DialogueDocument dialogue,
        ContentSnapshot content,
        AudioUrlResolver audio) =>
        dialogue.Turns
            .SelectMany(turn => turn.Segments)
            .Select(segment => segment.VocabularyId)
            .Where(id => id is not null && content.VocabularyById.ContainsKey(id))
            .Distinct()
            .Select(id => ApiContentMapper.ToDto(content.VocabularyById[id!], content, audio))
            .ToList();

    private static string Text(IReadOnlyDictionary<string, string> values, ContentSnapshot content) =>
        ContentSnapshot.Localize(values, content.Course.DefaultAssistanceLanguage);
}
