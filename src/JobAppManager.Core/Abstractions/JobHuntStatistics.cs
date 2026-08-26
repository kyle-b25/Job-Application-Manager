using JobAppManager.Core.Enums;

namespace JobAppManager.Core.Abstractions;

/// <summary>Everything the main menu needs to greet the user with numbers and graphs.</summary>
public record JobHuntStatistics
{
    public int TotalApplications { get; init; }

    /// <summary>Count per status. Contains an entry for every <see cref="ApplicationStatus"/>,
    /// zero included, so callers never have to handle a missing key.</summary>
    public IReadOnlyDictionary<ApplicationStatus, int> CountByStatus { get; init; }
        = new Dictionary<ApplicationStatus, int>();

    /// <summary>Count per interest level, zero-filled the same way.</summary>
    public IReadOnlyDictionary<InterestLevel, int> CountByInterest { get; init; }
        = new Dictionary<InterestLevel, int>();

    public int AppliedLast7Days { get; init; }

    public int AppliedLast30Days { get; init; }

    /// <summary>Applications per day over the recent window, ascending by date. Only days with
    /// at least one application appear; the chart layer fills the gaps.</summary>
    public IReadOnlyList<DailyApplicationCount> DailyCounts { get; init; }
        = Array.Empty<DailyApplicationCount>();
}

public record DailyApplicationCount(DateOnly Date, int Count);
