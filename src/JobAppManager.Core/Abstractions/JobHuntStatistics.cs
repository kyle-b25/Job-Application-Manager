using JobAppManager.Core.Enums;

namespace JobAppManager.Core.Abstractions;

/// <summary>Everything the dashboard needs to greet the user with numbers and graphs.</summary>
public record JobHuntStatistics
{
    public int TotalApplications { get; init; }

    /// <summary>Applications still in play: anything not Rejected.</summary>
    public int ActiveApplications { get; init; }

    /// <summary>Share of applications that ever reached Interview, 0..1. Every application was
    /// sent somewhere, so the denominator is all of them. Zero on an empty database.</summary>
    public double InterviewRate { get; init; }

    /// <summary>Share of applications that ever reached Rejected, 0..1. Same denominator as
    /// <see cref="InterviewRate"/>. Answered from history, so an application does not have to
    /// still be sitting in Rejected to count.</summary>
    public double RejectionRate { get; init; }

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

    /// <summary>Applications per calendar month, ascending. Only months with at least one
    /// application appear; the chart layer fills the gaps.</summary>
    public IReadOnlyList<MonthlyApplicationCount> MonthlyCounts { get; init; }
        = Array.Empty<MonthlyApplicationCount>();

    /// <summary>Average time applications spent sitting in each stage before moving on, derived
    /// from consecutive status changes. A stage nothing has ever left is omitted, so the chart
    /// layer decides how to show "no data yet" rather than being handed a misleading zero.</summary>
    public IReadOnlyList<StageDuration> AverageDaysInStage { get; init; }
        = Array.Empty<StageDuration>();
}

public record DailyApplicationCount(DateOnly Date, int Count);

/// <param name="Year">Calendar year.</param>
/// <param name="Month">1-12.</param>
public record MonthlyApplicationCount(int Year, int Month, int Count);

/// <param name="Stage">The stage being measured.</param>
/// <param name="AverageDays">Mean days from entering the stage to leaving it.</param>
/// <param name="SampleSize">How many completed stints that average is over.</param>
public record StageDuration(ApplicationStatus Stage, double AverageDays, int SampleSize);
