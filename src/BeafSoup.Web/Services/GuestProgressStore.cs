using System.Text.Json;
using BeafSoup.Contracts;
using Microsoft.JSInterop;

namespace BeafSoup.Web.Services;

public sealed class GuestProgressState
{
    public int SchemaVersion { get; set; } = 1;
    public string CourseId { get; set; } = "";
    public List<string> CompletedLessons { get; set; } = [];
    public List<AttemptRecord> Attempts { get; set; } = [];
}

public sealed record AttemptRecord(
    Guid Id,
    string QuestionId,
    string SelectedOptionId,
    bool IsCorrect,
    DateTimeOffset AnsweredAtUtc);

/// <summary>Server-side progress with a localStorage cache. The cache keeps learning usable
/// offline and carries any unsynced attempts to the next successful sync.</summary>
public sealed class GuestProgressStore(IJSRuntime jsRuntime, LearningApiClient api)
{
    private const string StorageKey = "beafsoup.guest-progress.v1";
    private const string GuestIdKey = "beafsoup.guest-id.v1";

    private string? _guestId;
    private GuestProgressState? _state;

    public string? StorageWarning { get; private set; }

    public bool SyncedToServer { get; private set; }

    public string? GuestId => _guestId;

    public async Task<GuestProgressState> LoadAsync(string courseId)
    {
        var local = await ReadLocalAsync(courseId) ?? new GuestProgressState { CourseId = courseId };
        await EnsureGuestIdAsync();
        var merged = await TrySyncAsync(local);
        _state = merged ?? local;
        return _state;
    }

    public async Task<GuestProgressState> RecordAttemptAsync(
        GuestProgressState state,
        QuestionDto question,
        GradeResultDto result)
    {
        state.Attempts.Add(new AttemptRecord(
            Guid.NewGuid(),
            question.Id,
            result.SelectedOptionId,
            result.IsCorrect,
            DateTimeOffset.UtcNow));
        await PersistAsync(state);
        return await TrySyncAsync(state) ?? state;
    }

    public async Task MarkLessonCompleteAsync(GuestProgressState state, string lessonId)
    {
        if (state.CompletedLessons.Contains(lessonId))
        {
            await PersistAsync(state);
            return;
        }

        state.CompletedLessons.Add(lessonId);
        await PersistAsync(state);
        await TrySyncAsync(state);
    }

    private async Task<GuestProgressState?> TrySyncAsync(GuestProgressState state)
    {
        try
        {
            var merged = await api.SyncProgressAsync(new ProgressSyncRequest(
                _guestId,
                state.CourseId,
                state.Attempts.Select(attempt => new AttemptRecordDto(
                    attempt.Id, attempt.QuestionId, attempt.SelectedOptionId, attempt.IsCorrect, attempt.AnsweredAtUtc)).ToList(),
                state.CompletedLessons));

            if (merged is null)
            {
                SyncedToServer = false;
                return null;
            }

            _guestId = merged.GuestId;
            await WriteGuestIdAsync(merged.GuestId);
            SyncedToServer = true;
            ApplyMerged(state, merged);
            await WriteLocalAsync(state);
            return state;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JSException or NotSupportedException or JsonException or InvalidOperationException)
        {
            // Offline or API unavailable: keep local progress and sync on the next load.
            SyncedToServer = false;
            return null;
        }
    }

    private static void ApplyMerged(GuestProgressState state, GuestProgressDto merged)
    {
        state.CompletedLessons = merged.CompletedLessons.ToList();
        state.Attempts = merged.Attempts
            .Select(attempt => new AttemptRecord(
                attempt.Id, attempt.QuestionId, attempt.SelectedOptionId, attempt.IsCorrect, attempt.AnsweredAtUtc))
            .ToList();
    }

    private async Task PersistAsync(GuestProgressState state)
    {
        await WriteLocalAsync(state);
    }

    private async Task<GuestProgressState?> ReadLocalAsync(string courseId)
    {
        try
        {
            var json = await jsRuntime.InvokeAsync<string?>("beafStorage.get", StorageKey);
            if (json is null)
            {
                StorageWarning = null;
                return null;
            }

            var state = JsonSerializer.Deserialize<GuestProgressState>(json);
            StorageWarning = null;
            return state?.SchemaVersion == 1 && state.CourseId == courseId ? state : null;
        }
        catch (Exception exception) when (exception is JSException or JsonException or InvalidOperationException)
        {
            StorageWarning = "本机进度暂时无法读取，本次学习仍可继续，但刷新后可能丢失进度。";
            return null;
        }
    }

    private async Task WriteLocalAsync(GuestProgressState state)
    {
        try
        {
            await jsRuntime.InvokeVoidAsync("beafStorage.set", StorageKey, JsonSerializer.Serialize(state));
            StorageWarning = null;
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException)
        {
            StorageWarning = "本机进度暂时无法保存，本次学习仍可继续，但刷新后可能丢失进度。";
        }
    }

    private async Task EnsureGuestIdAsync()
    {
        if (_guestId is not null) return;
        try
        {
            var stored = await jsRuntime.InvokeAsync<string?>("beafStorage.get", GuestIdKey);
            if (Guid.TryParse(stored, out _))
            {
                _guestId = stored;
                return;
            }
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException)
        {
            // Fall through to an in-memory id so syncing still works this session.
        }

        _guestId = Guid.NewGuid().ToString();
        await WriteGuestIdAsync(_guestId);
    }

    private async Task WriteGuestIdAsync(string guestId)
    {
        try
        {
            await jsRuntime.InvokeVoidAsync("beafStorage.set", GuestIdKey, guestId);
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException)
        {
            // The id stays in memory; progress still syncs while this tab is open.
        }
    }
}
