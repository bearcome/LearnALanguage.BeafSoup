using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BeafSoup.Data;

public static class PersistenceRegistration
{
    public const string SqliteProvider = "sqlite";
    public const string PostgresProvider = "postgresql";

    /// <summary>Reads <c>Database:Provider</c>; anything unknown falls back to SQLite so a
    /// misconfigured development machine still starts.</summary>
    public static string ResolveProvider(IConfiguration configuration)
    {
        var configured = configuration["Database:Provider"]?.Trim().ToLowerInvariant();
        return configured switch
        {
            "postgres" or "postgresql" or "npgsql" => PostgresProvider,
            _ => SqliteProvider
        };
    }

    public static string DefaultConnectionString(string provider) => provider == PostgresProvider
        ? "Host=localhost;Port=5432;Database=beafsoup;Username=beafsoup;Password=beafsoup"
        : "Data Source=artifacts/beafsoup.db";

    /// <summary>Typed overload so callers (and tests) keep strongly typed options.</summary>
    public static DbContextOptionsBuilder<TContext> Configure<TContext>(
        DbContextOptionsBuilder<TContext> builder,
        string provider,
        string connectionString)
        where TContext : DbContext
    {
        Configure((DbContextOptionsBuilder)builder, provider, connectionString);
        return builder;
    }

    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder builder, string provider, string connectionString)
    {
        if (provider == PostgresProvider)
        {
            return builder.UseNpgsql(connectionString);
        }

        var dataSource = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString).DataSource;
        if (!string.IsNullOrEmpty(dataSource))
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        }

        return builder.UseSqlite(connectionString);
    }

    public static IServiceCollection AddBeafSoupPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = ResolveProvider(configuration);
        var connectionString = configuration.GetConnectionString("BeafSoup") ?? DefaultConnectionString(provider);
        services.AddDbContext<BeafSoupDbContext>(builder => Configure(builder, provider, connectionString));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IGuestProgressRepository, GuestProgressRepository>();
        return services;
    }

    /// <summary>Creates the schema when it is missing. Both providers get the same logical
    /// schema from the shared model, so SQLite development data maps onto PostgreSQL.</summary>
    public static async Task InitializeAsync(BeafSoupDbContext db, CancellationToken cancellationToken = default)
    {
        if (db.Database.GetMigrations().Any())
        {
            await db.Database.MigrateAsync(cancellationToken);
            return;
        }

        await db.Database.EnsureCreatedAsync(cancellationToken);
    }
}
