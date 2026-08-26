using JobAppManager.Data;

namespace JobAppManager.Data.Tests;

/// <summary>A throwaway SQLite file per test. Deliberately file-backed rather than the
/// in-memory provider: cascade deletes and DateOnly round-tripping behave differently there,
/// and "survives a restart" cannot be proven against a store that dies with the connection.
/// </summary>
public sealed class SqliteTestFixture : IDisposable
{
    private readonly string _databasePath;

    public SqliteTestFixture()
    {
        _databasePath = Path.Combine(
            Path.GetTempPath(),
            $"jobapps-test-{Guid.NewGuid():N}.db");

        // Apply migrations once; every context handed out afterwards opens the same file.
        using var context = CreateContext();
        DatabaseInitializer.Initialize(context);
    }

    /// <summary>A fresh context over the same file. Call twice to simulate an app restart.</summary>
    public JobAppContext CreateContext() =>
        new(DatabaseInitializer.BuildOptions(_databasePath));

    public void Dispose()
    {
        // SQLite keeps the file handle until the pool is cleared.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        foreach (var path in new[] { _databasePath, _databasePath + "-shm", _databasePath + "-wal" })
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // A leaked handle should not fail an otherwise passing test run.
            }
        }
    }
}
