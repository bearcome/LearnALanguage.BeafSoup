namespace BeafSoup.Content;

public static class ContentValidator
{
    public static void Validate(ContentSnapshot snapshot)
    {
        var errors = new List<string>();
        var course = snapshot.Course;

        Require(!string.IsNullOrWhiteSpace(course.Id), "课程 ID 不能为空。", errors);
        Require(course.SchemaVersion == 1, "不支持的课程 Schema 版本。", errors);
        Require(course.Title.ContainsKey(course.DefaultAssistanceLanguage), "课程缺少默认辅助语言标题。", errors);
        EnsureUnique(course.LessonIds, "课时", errors);
        EnsureUnique(snapshot.Lessons.Select(lesson => lesson.Order), "课时顺序", errors);
        Require(snapshot.Lessons.Select(lesson => lesson.Order).OrderBy(order => order)
            .SequenceEqual(Enumerable.Range(1, snapshot.Lessons.Count)), "课时顺序必须从 1 开始连续编号。", errors);
        EnsureUnique(snapshot.Vocabulary.Select(item => item.Id), "词汇", errors);
        EnsureUnique(snapshot.Questions.Select(item => item.Id), "题目", errors);
        EnsureUnique(snapshot.Sentences.Select(item => item.Id), "句子", errors);

        foreach (var lesson in snapshot.Lessons)
        {
            Require(lesson.CourseId == course.Id, $"课时 {lesson.Id} 的课程引用不正确。", errors);
            Require(course.LessonIds.Contains(lesson.Id), $"课时 {lesson.Id} 未列入课程目录。", errors);
            EnsureUnique(lesson.VocabularyIds, $"课时 {lesson.Id} 词汇", errors);
            EnsureUnique(lesson.QuestionIds, $"课时 {lesson.Id} 题目", errors);

            foreach (var id in lesson.VocabularyIds)
            {
                Require(snapshot.VocabularyById.ContainsKey(id), $"课时 {lesson.Id} 引用了不存在的词汇 {id}。", errors);
            }

            foreach (var id in lesson.QuestionIds)
            {
                Require(snapshot.QuestionsById.ContainsKey(id), $"课时 {lesson.Id} 引用了不存在的题目 {id}。", errors);
            }

            foreach (var sentence in lesson.Sentences)
            {
                ValidateSentence(sentence, snapshot, course, errors);
            }
        }

        foreach (var id in course.LessonIds)
        {
            Require(snapshot.LessonsById.ContainsKey(id), $"课程目录引用了不存在的课时 {id}。", errors);
        }

        foreach (var entry in snapshot.Vocabulary)
        {
            Require(!string.IsNullOrWhiteSpace(entry.Lemma), $"词汇 {entry.Id} 缺少原型。", errors);
            Require(entry.Language == course.TargetLanguage, $"词汇 {entry.Id} 的目标语言不正确。", errors);
            Require(entry.Translations.ContainsKey(course.DefaultAssistanceLanguage), $"词汇 {entry.Id} 缺少中文释义。", errors);
        }

        foreach (var question in snapshot.Questions)
        {
            ValidateQuestion(question, snapshot, course, errors);
        }

        EnsureUnique(snapshot.DialogueTurns.Select(turn => turn.Id), "对话台词", errors);
        foreach (var dialogue in snapshot.Dialogues)
        {
            ValidateDialogue(dialogue, snapshot, course, errors);
        }

        if (errors.Count > 0)
        {
            throw new InvalidDataException("课程内容校验失败：\n" + string.Join("\n", errors));
        }
    }

    private static void ValidateSentence(
        SentenceDocument sentence,
        ContentSnapshot snapshot,
        CourseDocument course,
        List<string> errors)
    {
        Require(sentence.Language == course.TargetLanguage, $"句子 {sentence.Id} 的目标语言不正确。", errors);
        Require(!string.IsNullOrWhiteSpace(sentence.Text), $"句子 {sentence.Id} 正文为空。", errors);
        Require(sentence.Translations.ContainsKey(course.DefaultAssistanceLanguage), $"句子 {sentence.Id} 缺少中文翻译。", errors);
        Require(string.Concat(sentence.Segments.Select(segment => segment.Text)) == sentence.Text,
            $"句子 {sentence.Id} 的分词拼接结果与正文不一致。", errors);

        foreach (var segment in sentence.Segments.Where(segment => segment.VocabularyId is not null))
        {
            Require(snapshot.VocabularyById.ContainsKey(segment.VocabularyId!),
                $"句子 {sentence.Id} 引用了不存在的词汇 {segment.VocabularyId}。", errors);
        }
    }

    private static void ValidateQuestion(
        QuestionDocument question,
        ContentSnapshot snapshot,
        CourseDocument course,
        List<string> errors)
    {
        Require(question.Type == "singleChoice", $"题目 {question.Id} 不是单选题。", errors);
        Require(question.TargetLanguage == course.TargetLanguage, $"题目 {question.Id} 的目标语言不正确。", errors);
        Require(question.Prompt.ContainsKey(question.PromptLanguage), $"题目 {question.Id} 缺少题干。", errors);
        Require(question.Instruction.ContainsKey(course.DefaultAssistanceLanguage), $"题目 {question.Id} 缺少中文说明。", errors);
        Require(question.Explanation.ContainsKey(course.DefaultAssistanceLanguage), $"题目 {question.Id} 缺少中文解析。", errors);
        Require(question.Options.Count == 4, $"题目 {question.Id} 必须包含 4 个选项。", errors);
        EnsureUnique(question.Options.Select(option => option.Id), $"题目 {question.Id} 选项", errors);
        Require(question.Options.Select(option => option.Text).Distinct().Count() == question.Options.Count,
            $"题目 {question.Id} 存在重复选项文本。", errors);
        Require(question.Options.Any(option => option.Id == question.CorrectOptionId),
            $"题目 {question.Id} 的正确答案不在选项中。", errors);

        foreach (var id in question.RelatedVocabularyIds)
        {
            Require(snapshot.VocabularyById.ContainsKey(id), $"题目 {question.Id} 引用了不存在的词汇 {id}。", errors);
        }

        if (question.RelatedSentenceId is not null)
        {
            Require(snapshot.FindSentence(question.RelatedSentenceId) is not null,
                $"题目 {question.Id} 引用了不存在的句子 {question.RelatedSentenceId}。", errors);
        }
    }

    private static void ValidateDialogue(
        DialogueDocument dialogue,
        ContentSnapshot snapshot,
        CourseDocument course,
        List<string> errors)
    {
        Require(dialogue.SchemaVersion == 1, $"对话 {dialogue.Id} 的 Schema 版本不正确。", errors);
        Require(dialogue.CourseId == course.Id, $"对话 {dialogue.Id} 的课程引用不正确。", errors);
        Require(dialogue.Language == course.TargetLanguage, $"对话 {dialogue.Id} 的目标语言不正确。", errors);
        Require(dialogue.Title.ContainsKey(course.DefaultAssistanceLanguage), $"对话 {dialogue.Id} 缺少中文标题。", errors);
        Require(dialogue.Scenario.ContainsKey(course.DefaultAssistanceLanguage), $"对话 {dialogue.Id} 缺少中文场景说明。", errors);
        Require(dialogue.Speakers.Count >= 2, $"对话 {dialogue.Id} 至少需要两个说话人。", errors);
        Require(dialogue.Turns.Count > 0, $"对话 {dialogue.Id} 没有台词。", errors);
        EnsureUnique(dialogue.Speakers.Select(speaker => speaker.Id), $"对话 {dialogue.Id} 说话人", errors);

        foreach (var lessonId in dialogue.LessonIds)
        {
            Require(snapshot.LessonsById.ContainsKey(lessonId), $"对话 {dialogue.Id} 引用了不存在的课时 {lessonId}。", errors);
        }

        foreach (var speaker in dialogue.Speakers)
        {
            Require(!string.IsNullOrWhiteSpace(speaker.Name), $"对话 {dialogue.Id} 的说话人 {speaker.Id} 缺少姓名。", errors);
            Require(!string.IsNullOrWhiteSpace(speaker.Voice), $"对话 {dialogue.Id} 的说话人 {speaker.Id} 缺少音色。", errors);
            Require(double.TryParse(speaker.Rate, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var rate) && rate is >= 0.5 and <= 2,
                $"对话 {dialogue.Id} 的说话人 {speaker.Id} 语速不在 0.5 到 2 之间。", errors);
        }

        var speakerIds = dialogue.Speakers.Select(speaker => speaker.Id).ToHashSet();
        foreach (var turn in dialogue.Turns)
        {
            Require(speakerIds.Contains(turn.SpeakerId), $"对话 {dialogue.Id} 的台词 {turn.Id} 引用了不存在的说话人 {turn.SpeakerId}。", errors);
            Require(!string.IsNullOrWhiteSpace(turn.Text), $"对话 {dialogue.Id} 的台词 {turn.Id} 正文为空。", errors);
            Require(turn.Translations.ContainsKey(course.DefaultAssistanceLanguage), $"对话 {dialogue.Id} 的台词 {turn.Id} 缺少中文翻译。", errors);
            Require(string.Concat(turn.Segments.Select(segment => segment.Text)) == turn.Text,
                $"对话 {dialogue.Id} 的台词 {turn.Id} 分词拼接结果与正文不一致。", errors);
            Require(turn.PauseAfterMs is >= 0 and <= 15000, $"对话 {dialogue.Id} 的台词 {turn.Id} 跟读停顿超出 0 到 15000 毫秒。", errors);

            foreach (var segment in turn.Segments.Where(segment => segment.VocabularyId is not null))
            {
                Require(snapshot.VocabularyById.ContainsKey(segment.VocabularyId!),
                    $"对话 {dialogue.Id} 的台词 {turn.Id} 引用了不存在的词汇 {segment.VocabularyId}。", errors);
            }
        }
    }

    private static void EnsureUnique<T>(IEnumerable<T> values, string label, List<string> errors) where T : notnull
    {
        var duplicate = values.GroupBy(value => value).FirstOrDefault(group => group.Count() > 1);
        Require(duplicate is null, $"{label}存在重复值：{duplicate?.Key?.ToString() ?? ""}。", errors);
    }

    private static void Require(bool condition, string message, List<string> errors)
    {
        if (!condition)
        {
            errors.Add(message);
        }
    }
}
