using Microsoft.EntityFrameworkCore;

namespace BeafSoup.Data;

/// <summary>Schema stays inside what SQLite and PostgreSQL both support: Guid keys,
/// bounded strings, DateTimeOffset timestamps, and no provider-specific column types.</summary>
public sealed class BeafSoupDbContext(DbContextOptions<BeafSoupDbContext> options) : DbContext(options)
{
    public DbSet<GuestProfile> GuestProfiles => Set<GuestProfile>();
    public DbSet<GuestAttempt> GuestAttempts => Set<GuestAttempt>();
    public DbSet<GuestCompletedLesson> GuestCompletedLessons => Set<GuestCompletedLesson>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GuestProfile>(entity =>
        {
            entity.HasKey(profile => profile.Id);
            entity.Property(profile => profile.CreatedAtUtc).IsRequired();
            entity.Property(profile => profile.UpdatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<GuestAttempt>(entity =>
        {
            entity.HasKey(attempt => attempt.Id);
            entity.Property(attempt => attempt.QuestionId).HasMaxLength(64).IsRequired();
            entity.Property(attempt => attempt.SelectedOptionId).HasMaxLength(64).IsRequired();
            entity.Property(attempt => attempt.AnsweredAtUtc).IsRequired();
            entity.HasIndex(attempt => attempt.GuestProfileId);
            entity.HasIndex(attempt => new { attempt.GuestProfileId, attempt.QuestionId });
            entity.HasOne<GuestProfile>()
                .WithMany(profile => profile.Attempts)
                .HasForeignKey(attempt => attempt.GuestProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GuestCompletedLesson>(entity =>
        {
            entity.HasKey(lesson => lesson.Id);
            entity.Property(lesson => lesson.LessonId).HasMaxLength(64).IsRequired();
            entity.Property(lesson => lesson.CompletedAtUtc).IsRequired();
            entity.HasIndex(lesson => new { lesson.GuestProfileId, lesson.LessonId }).IsUnique();
            entity.HasOne<GuestProfile>()
                .WithMany(profile => profile.CompletedLessons)
                .HasForeignKey(lesson => lesson.GuestProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
