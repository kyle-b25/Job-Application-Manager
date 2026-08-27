using JobAppManager.Core.Abstractions;
using JobAppManager.Core.Entities;
using JobAppManager.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace JobAppManager.Data.Repositories;

public class ApplicationRepository : IApplicationRepository
{
    private readonly JobAppContext _context;
    private readonly TimeProvider _timeProvider;

    /// <param name="timeProvider">Only <see cref="GetStatisticsAsync"/> reads it, for the
    /// "last N days" windows. Optional so existing call sites are unaffected.</param>
    public ApplicationRepository(JobAppContext context, TimeProvider? timeProvider = null)
    {
        _context = context;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<Application> AddAsync(
        Application application,
        CancellationToken cancellationToken = default)
    {
        // Every application starts its history at whatever status it was created in, so the
        // timeline reads as a complete sequence even for a row nobody ever advances.
        if (application.StatusHistory.Count == 0)
        {
            application.StatusHistory.Add(new StatusChange { Status = application.Status });
        }

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

    public async Task<bool> ChangeStatusAsync(
        int applicationId,
        ApplicationStatus newStatus,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        var application = await _context.Applications
            .FirstOrDefaultAsync(a => a.Id == applicationId, cancellationToken);

        if (application is null)
        {
            return false;
        }

        // Re-entering the same status is not a transition; recording it would inflate the
        // history and put a zero-length stint into the stage-duration average.
        if (application.Status == newStatus)
        {
            return true;
        }

        application.Status = newStatus;

        // The round number and the interview date only mean anything inside Interview.
        if (newStatus != ApplicationStatus.Interview)
        {
            application.InterviewRound = null;
            application.InterviewDate = null;
        }

        _context.StatusChanges.Add(new StatusChange
        {
            ApplicationId = applicationId,
            Status = newStatus,
            Note = note
        });

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task<Application?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        _context.Applications
            .Include(a => a.Contacts)
            .Include(a => a.StatusHistory.OrderBy(s => s.ChangedUtc))
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
            var pattern = ToLikePattern(filter.CompanyContains);
            query = query.Where(a => EF.Functions.Like(a.CompanyName, pattern, LikeEscape));
        }

        if (!string.IsNullOrWhiteSpace(filter.JobTitleContains))
        {
            var pattern = ToLikePattern(filter.JobTitleContains);
            query = query.Where(a => EF.Functions.Like(a.JobTitle, pattern, LikeEscape));
        }

        if (!string.IsNullOrWhiteSpace(filter.TextContains))
        {
            var pattern = ToLikePattern(filter.TextContains);
            query = query.Where(a =>
                EF.Functions.Like(a.CompanyName, pattern, LikeEscape)
                || EF.Functions.Like(a.JobTitle, pattern, LikeEscape));
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
        // Local, not UTC, and deliberately so: DateApplied is a calendar date the user typed in,
        // so "the last 7 days" has to be counted in the user's own days, not UTC's.
        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
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

        var monthlyCounts = await _context.Applications
            .GroupBy(a => new { a.DateApplied.Year, a.DateApplied.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
            .OrderBy(x => x.Year).ThenBy(x => x.Month)
            .ToListAsync(cancellationToken);

        var total = statusCounts.Sum(x => x.Count);

        var byStatus = ZeroFill<ApplicationStatus>(
            statusCounts.ToDictionary(x => x.Key, x => x.Count));

        var active = total - byStatus[ApplicationStatus.Rejected];

        // Rates are answered from history, not the current status: an application that was
        // interviewed and then rejected still counts as an interview reached. Every application
        // was sent somewhere, so all of them are in the denominator.
        var reached = await _context.StatusChanges
            .GroupBy(s => s.ApplicationId)
            .Select(g => new
            {
                Interviewed = g.Any(s => s.Status == ApplicationStatus.Interview),
                RejectedEver = g.Any(s => s.Status == ApplicationStatus.Rejected)
            })
            .ToListAsync(cancellationToken);

        return new JobHuntStatistics
        {
            TotalApplications = total,
            ActiveApplications = active,
            InterviewRate = Rate(reached.Count(x => x.Interviewed), reached.Count),
            RejectionRate = Rate(reached.Count(x => x.RejectedEver), reached.Count),
            CountByStatus = byStatus,
            CountByInterest = ZeroFill<InterestLevel>(
                interestCounts.ToDictionary(x => x.Key, x => x.Count)),
            AppliedLast7Days = appliedLast7,
            AppliedLast30Days = appliedLast30,
            DailyCounts = dailyCounts
                .Select(x => new DailyApplicationCount(x.Date, x.Count))
                .ToList(),
            MonthlyCounts = monthlyCounts
                .Select(x => new MonthlyApplicationCount(x.Year, x.Month, x.Count))
                .ToList()
        };
    }

    /// <summary>Share, or zero when there is nothing to take a share of.</summary>
    private static double Rate(int numerator, int denominator) =>
        denominator == 0 ? 0d : (double)numerator / denominator;

    /// <summary>SQLite has no default LIKE escape character, so one has to be declared per call.</summary>
    private const string LikeEscape = "\\";

    /// <summary>Wraps a search term in wildcards, escaping any the user actually typed. Without
    /// this a search for "%" matches every row and "_" matches any single character - a search
    /// box should look for the characters someone typed, not treat them as a pattern.</summary>
    private static string ToLikePattern(string term)
    {
        // The escape character goes first, or it would escape the escapes added after it.
        var escaped = term
            .Replace(@"\", @"\\")
            .Replace("%", @"\%")
            .Replace("_", @"\_");

        return $"%{escaped}%";
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
