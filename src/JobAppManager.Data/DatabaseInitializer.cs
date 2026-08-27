using Microsoft.EntityFrameworkCore;

namespace JobAppManager.Data;

public static class DatabaseInitializer
{
    /// <summary>Builds options pointing at the given SQLite file, or the default location.</summary>
    public static DbContextOptions<JobAppContext> BuildOptions(string? databasePath = null)
    {
        var connectionString = databasePath is null
            ? DbPathProvider.GetDefaultConnectionString()
            : DbPathProvider.GetConnectionString(databasePath);

        return new DbContextOptionsBuilder<JobAppContext>()
            .UseSqlite(connectionString)
            .Options;
    }

    /// <summary>Applies any pending migrations. Call once at startup, before the first query.</summary>
    public static void Initialize(JobAppContext context) => context.Database.Migrate();

    /// <summary>Convenience for startup: build options, open a context, migrate, return it.
    /// The caller owns the returned context.</summary>
    public static JobAppContext CreateAndMigrate(
        string? databasePath = null,
        TimeProvider? timeProvider = null)
    {
        var context = new JobAppContext(BuildOptions(databasePath), timeProvider);
        Initialize(context);
        return context;
    }
}
