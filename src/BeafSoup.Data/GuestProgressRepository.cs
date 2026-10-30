using BeafSoup.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BeafSoup.Data;

public interface IGuestProgressRepository
{
    Task<GuestProgressDto?> GetAsync(Guid guestId, string courseId, CancellationToken cancellationToken);

    Task<GuestProgressDto> SyncAsync(ProgressSyncRequest request, CancellationToken cancellationToken);
}

public sealed class GuestProgressRepository(BeafSoupDbContext db, TimeProvider timeProvider) : IGuestProgressRepository
{
    public async Task<GuestProgressDto?> GetAsync(Guid guestId, string courseId, CancellationToken cancellationToken)
    {
        var profile = await LoadAsync(guestId, cancellationToken);
        return profile is null ? null : ToDto(profile, courseId);
    }

    public async Task<GuestProgressDto> SyncAsync(ProgressSyncRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var guestId = ParseGuestId(request.GuestId);
        var profile = await LoadAsync(guestId, cancellationToken);
        if (profile is null)
        {
            profile = new GuestProfile { Id = guestId, CreatedAtUtc = now, UpdatedAtUtc = now };
            db.GuestProfiles.Add(profile);
        }

        var incoming = request.Attempts ?? [];
        var incomingIds = incoming.Select(attempt => attempt.Id).Distinct().ToList();
        var takenElsewhere = (await db.GuestAttempts
                .Where(attempt => incomingIds.Contains(attempt.Id) && attempt.GuestProfileId != guestId)
                .Select(attempt => attempt.Id)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var known = profile.Attempts.Select(attempt => attempt.Id).ToHashSet();
        foreach (var attempt in incoming)
        {
            if (known.Contains(attempt.Id) || takenElsewhere.Contains(attempt.Id)) continue;
            known.Add(attempt.Id);
            profile.Attempts.Add(new GuestAttempt
            {
                Id = attempt.Id,
                GuestProfileId = guestId,
                QuestionId = attempt.QuestionId,
                SelectedOptionId = attempt.SelectedOptionId,
                IsCorrect = attempt.IsCorrect,
                AnsweredAtUtc = attempt.AnsweredAtUtc
            });
        }

        var completed = profile.CompletedLessons.Select(lesson => lesson.LessonId).ToHashSet();
        foreach (var lessonId in (request.CompletedLessons ?? []).Where(lessonId => !string.IsNullOrWhiteSpace(lessonId)))
        {
            if (!completed.Add(lessonId)) continue;
            profile.CompletedLessons.Add(new GuestCompletedLesson
            {
                Id = Guid.NewGuid(),
                GuestProfileId = guestId,
                LessonId = lessonId,
                CompletedAtUtc = now
            });
        }

        profile.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(profile, request.CourseId);
    }

    private Task<GuestProfile?> LoadAsync(Guid guestId, CancellationToken cancellationToken) =>
        db.GuestProfiles
            .Include(profile => profile.Attempts)
            .Include(profile => profile.CompletedLessons)
            .FirstOrDefaultAsync(profile => profile.Id == guestId, cancellationToken);

    private static Guid ParseGuestId(string? guestId) =>
        Guid.TryParse(guestId, out var parsed) ? parsed : Guid.NewGuid();

    private static GuestProgressDto ToDto(GuestProfile profile, string courseId) => new(
        profile.Id.ToString(),
        courseId,
        profile.CompletedLessons.OrderBy(lesson => lesson.CompletedAtUtc).Select(lesson => lesson.LessonId).ToList(),
        profile.Attempts.OrderBy(attempt => attempt.AnsweredAtUtc)
            .Select(attempt => new AttemptRecordDto(
                attempt.Id, attempt.QuestionId, attempt.SelectedOptionId, attempt.IsCorrect, attempt.AnsweredAtUtc))
            .ToList(),
        profile.UpdatedAtUtc);
}
