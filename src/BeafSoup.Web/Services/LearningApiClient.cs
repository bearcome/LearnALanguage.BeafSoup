using System.Net.Http.Json;
using BeafSoup.Contracts;

namespace BeafSoup.Web.Services;

public sealed class LearningApiClient(HttpClient httpClient)
{
    public Task<CourseSummaryDto?> GetCourseSummaryAsync(CancellationToken cancellationToken = default) =>
        httpClient.GetFromJsonAsync<CourseSummaryDto>("api/v1/courses", cancellationToken);

    public Task<CourseDetailDto?> GetCourseAsync(string courseId, CancellationToken cancellationToken = default) =>
        httpClient.GetFromJsonAsync<CourseDetailDto>($"api/v1/courses/{courseId}", cancellationToken);

    public Task<LessonDetailDto?> GetLessonAsync(string lessonId, CancellationToken cancellationToken = default) =>
        GetOrThrowAsync<LessonDetailDto>($"api/v1/lessons/{lessonId}", cancellationToken);

    public Task<List<DialogueSummaryDto>?> GetDialoguesAsync(CancellationToken cancellationToken = default) =>
        httpClient.GetFromJsonAsync<List<DialogueSummaryDto>>("api/v1/dialogues", cancellationToken);

    public Task<DialogueDetailDto?> GetDialogueAsync(string dialogueId, CancellationToken cancellationToken = default) =>
        GetOrThrowAsync<DialogueDetailDto>($"api/v1/dialogues/{dialogueId}", cancellationToken);

    public Task<GuestProgressDto?> SyncProgressAsync(ProgressSyncRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<GuestProgressDto>("api/v1/progress/sync", request, cancellationToken);

    private async Task<T?> PostAsync<T>(string url, object payload, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(url, payload, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    public Task<List<QuestionDto>?> GetLessonQuestionsAsync(string lessonId, CancellationToken cancellationToken = default) =>
        GetOrThrowAsync<List<QuestionDto>>($"api/v1/lessons/{lessonId}/questions", cancellationToken);

    private async Task<T?> GetOrThrowAsync<T>(string url, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(url, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException("请求的内容不存在，请返回课程首页重新选择。");
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    public Task<List<QuestionDto>?> GetCourseQuestionsAsync(string courseId, string? lessonId = null, CancellationToken cancellationToken = default)
    {
        var query = lessonId is null ? "" : $"?lessonId={lessonId}";
        return httpClient.GetFromJsonAsync<List<QuestionDto>>($"api/v1/courses/{courseId}/questions{query}", cancellationToken);
    }

    public async Task<GradeResultDto> GradeAsync(string questionId, string selectedOptionId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            $"api/v1/questions/{questionId}/grade",
            new GradeRequest(selectedOptionId),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GradeResultDto>(cancellationToken)
            ?? throw new InvalidOperationException("判分服务返回了空结果。");
    }
}
