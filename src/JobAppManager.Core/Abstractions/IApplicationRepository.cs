using JobAppManager.Core.Entities;
using JobAppManager.Core.Enums;

namespace JobAppManager.Core.Abstractions;

/// <summary>Persistence surface for the three SRS screens. Lives in Core so the UI can depend
/// on it without referencing EF Core.</summary>
public interface IApplicationRepository
{
    /// <summary>Inserts a new application together with any contacts attached to it. Returns the saved entity with its assigned <see cref="Application.Id"/>.</summary>
    Task<Application> AddAsync(Application application, CancellationToken cancellationToken = default);

    /// <summary>Persists changes to an existing application and its children.</summary>
    Task UpdateAsync(Application application, CancellationToken cancellationToken = default);

    /// <summary>Deletes an application and, by cascade, its contacts and status history.
    /// Returns false if no application with that id exists.</summary>
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Moves an application to <paramref name="newStatus"/> and records the move in its
    /// history. This is the only supported way to advance an application through the pipeline -
    /// assigning <see cref="Application.Status"/> directly leaves the history behind. Does nothing
    /// if the application is already in that status. Returns false if no such application exists.</summary>
    Task<bool> ChangeStatusAsync(
        int applicationId,
        ApplicationStatus newStatus,
        string? note = null,
        CancellationToken cancellationToken = default);

    /// <summary>Loads one application with its contacts and status history, or null.</summary>
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
