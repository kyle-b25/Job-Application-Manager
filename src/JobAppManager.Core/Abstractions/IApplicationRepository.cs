using JobAppManager.Core.Entities;

namespace JobAppManager.Core.Abstractions;

/// <summary>Persistence surface for the three SRS screens. Lives in Core so the UI can depend
/// on it without referencing EF Core.</summary>
public interface IApplicationRepository
{
    /// <summary>Inserts a new application together with any submitted items and contacts
    /// attached to it. Returns the saved entity with its assigned <see cref="Application.Id"/>.</summary>
    Task<Application> AddAsync(Application application, CancellationToken cancellationToken = default);

    /// <summary>Persists changes to an existing application and its children.</summary>
    Task UpdateAsync(Application application, CancellationToken cancellationToken = default);

    /// <summary>Deletes an application and, by cascade, its submitted items and contacts.
    /// Returns false if no application with that id exists.</summary>
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Loads one application with its submitted items and contacts, or null.</summary>
    Task<Application?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Filtered, sorted list for the spreadsheet page. Children are not loaded.</summary>
    Task<IReadOnlyList<Application>> QueryAsync(
        ApplicationFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>Aggregates for the main menu. Returns zeroed statistics on an empty database.</summary>
    /// <param name="dailyCountWindowDays">How far back the daily series reaches.</param>
    Task<JobHuntStatistics> GetStatisticsAsync(
        int dailyCountWindowDays = 30,
        CancellationToken cancellationToken = default);
}
