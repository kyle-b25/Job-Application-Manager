namespace JobAppManager.Data;

/// <summary>Resolves where the SQLite file lives. Kept out of the DbContext so tests can point
/// at a temp file without touching the real database.</summary>
public static class DbPathProvider
{
    public const string AppFolderName = "JobApplicationManager";
    public const string DatabaseFileName = "jobapps.db";

    /// <summary>%LOCALAPPDATA%\JobApplicationManager\jobapps.db. Creates the folder if needed.</summary>
    public static string GetDefaultDatabasePath()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppFolderName);

        Directory.CreateDirectory(folder);

        return Path.Combine(folder, DatabaseFileName);
    }

    /// <summary>Connection string for a database file at <paramref name="databasePath"/>.</summary>
    public static string GetConnectionString(string databasePath) => $"Data Source={databasePath}";

    /// <summary>Connection string for the default location.</summary>
    public static string GetDefaultConnectionString() =>
        GetConnectionString(GetDefaultDatabasePath());
}
