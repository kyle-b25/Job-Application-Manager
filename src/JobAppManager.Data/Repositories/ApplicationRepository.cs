using JobAppManager.Core.Abstractions;
using JobAppManager.Core.Entities;
using JobAppManager.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace JobAppManager.Data.Repositories;

public class ApplicationRepository : IApplicationRepository
{
    private readonly JobAppContext _context;

    public ApplicationRepository(JobAppContext context) => _context = context;

    public async Task<Application> AddAsync(
        Application application,
        CancellationToken cancellationToken = default)
    {
        _context.Applications.Add(application);
        await _context.SaveChangesAsync(cancellationToken);
        return application;
    }

    public async Task UpdateAsync(
        Application application,
        CancellationToken cancellationToken = default)
    {
        // Attach only when the caller handed us a detached graph; an entity loaded by this
        // context is already tracked and re-attaching it would throw.
        if (_context.Entry(application).State == EntityState.Detached)
        {
            _context.Applications.Update(application);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var application = await _context.Applications
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (application is null)
        {
            return false;
        }

        _context.Applications.Remove(application);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task<Application?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        _context.Applications
            .Include(a => a.SubmittedItems)
            .Include(a => a.Contacts)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Application>> QueryAsync(
        ApplicationFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Applications.AsNoTracking().AsQueryable();

        if (filter.Status is { } status)
        {
            query = query.Where(a => a.Status == status);
        }

        if (filter.InterestLevel is { } interest)
        {
            query = query.Where(a => a.InterestLevel == interest);
        }

        if (filter.AppliedOnOrAfter is { } from)
        {
            query = query.Where(a => a.DateApplied >= from);
        }

        if (filter.AppliedOnOrBefore is { } to)
        {
            query = query.Where(a => a.DateApplied <= to);
        }

        // LIKE rather than Contains: EF translates Contains to instr(), which is case-sensitive
        // in SQLite, while LIKE is case-insensitive for ASCII - what a search box should do.
        if (!string.IsNullOrWhiteSpace(filter.CompanyContains))
        {
            var pattern = $"%{filter.CompanyContains}%";
            query = query.Where(a => EF.Functions.Like(a.CompanyName, pattern));
        }

        if (!string.IsNullOrWhiteSpace(filter.JobTitleContains))
        {
            var pattern = $"%{filter.JobTitleContains}%";
            query = query.Where(a => EF.Functions.Like(a.JobTitle, pattern));
        }

        if (filter.FromJobFair is { } jobFair)
        {
            query = query.Where(a => a.FromJobFair == jobFair);
        }

        query = ApplySort(query, filter);

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<JobHuntStatistics> GetStatisticsAsync(
        int dailyCountWindowDays = 30,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var last7 = today.AddDays(-6);   // inclusive of today, so 7 calendar days
        var last30 = today.AddDays(-29);
        var windowStart = today.AddDays(-(Math.Max(dailyCountWindowDays, 1) - 1));

        // Aggregated in SQL - the main menu must not pay for loading every row.
        var statusCounts = await _context.Applications
            .GroupBy(a => a.Status)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var interestCounts = await _context.Applications
            .GroupBy(a => a.InterestLevel)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var dailyCounts = await _context.Applications
            .Where(a => a.DateApplied >= windowStart && a.DateApplied <= today)
            .GroupBy(a => a.DateApplied)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .OrderBy(x => x.Date)
            .ToListAsync(cancellationToken);

        var appliedLast7 = await _context.Applications
            .CountAsync(a => a.DateApplied >= last7 && a.DateApplied <= today, cancellationToken);

        var appliedLast30 = await _context.Applications
            .CountAsync(a => a.DateApplied >= last30 && a.DateApplied <= today, cancellationToken);

        var total = statusCounts.Sum(x => x.Count);

        return new JobHuntStatistics
        {
            TotalApplications = total,
            CountByStatus = ZeroFill<ApplicationStatus>(
                statusCounts.ToDictionary(x => x.Key, x => x.Count)),
            CountByInterest = ZeroFill<InterestLevel>(
                interestCounts.ToDictionary(x => x.Key, x => x.Count)),
            AppliedLast7Days = appliedLast7,
            AppliedLast30Days = appliedLast30,
            DailyCounts = dailyCounts
                .Select(x => new DailyApplicationCount(x.Date, x.Count))
                .ToList()
        };
    }

    private static IQueryable<Application> ApplySort(
        IQueryable<Application> query,
        ApplicationFilter filter)
    {
        // Every sort falls back to Id so results are deterministic when the sort key ties
        // (several applications on the same day, say).
        return (filter.SortBy, filter.SortDescending) switch
        {
            (ApplicationSortField.DateApplied, true) =>
                query.OrderByDescending(a => a.DateApplied).ThenByDescending(a => a.Id),
            (ApplicationSortField.DateApplied, false) =>
                query.OrderBy(a => a.DateApplied).ThenBy(a => a.Id),

            (ApplicationSortField.CompanyName, true) =>
                query.OrderByDescending(a => a.CompanyName).ThenByDescending(a => a.Id),
            (ApplicationSortField.CompanyName, false) =>
                query.OrderBy(a => a.CompanyName).ThenBy(a => a.Id),

            (ApplicationSortField.JobTitle, true) =>
                query.OrderByDescending(a => a.JobTitle).ThenByDescending(a => a.Id),
            (ApplicationSortField.JobTitle, false) =>
                query.OrderBy(a => a.JobTitle).ThenBy(a => a.Id),

            (ApplicationSortField.Location, true) =>
                query.OrderByDescending(a => a.Location).ThenByDescending(a => a.Id),
            (ApplicationSortField.Location, false) =>
                query.OrderBy(a => a.Location).ThenBy(a => a.Id),

            (ApplicationSortField.Status, true) =>
                query.OrderByDescending(a => a.Status).ThenByDescending(a => a.Id),
            (ApplicationSortField.Status, false) =>
                query.OrderBy(a => a.Status).ThenBy(a => a.Id),

            (ApplicationSortField.InterestLevel, true) =>
                query.OrderByDescending(a => a.InterestLevel).ThenByDescending(a => a.Id),
            (ApplicationSortField.InterestLevel, false) =>
                query.OrderBy(a => a.InterestLevel).ThenBy(a => a.Id),

            _ => query.OrderByDescending(a => a.DateApplied).ThenByDescending(a => a.Id)
        };
    }

    /// <summary>Guarantees an entry for every enum member so the UI never handles a missing key.</summary>
    private static IReadOnlyDictionary<TEnum, int> ZeroFill<TEnum>(IDictionary<TEnum, int> counts)
        where TEnum : struct, Enum
    {
        var result = new Dictionary<TEnum, int>();

        foreach (var value in Enum.GetValues<TEnum>())
        {
            result[value] = counts.TryGetValue(value, out var count) ? count : 0;
        }

        return result;
    }
}
