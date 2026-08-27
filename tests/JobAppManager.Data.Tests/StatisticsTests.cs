using JobAppManager.TestSupport;
using JobAppManager.Core.Enums;
using JobAppManager.Data.Repositories;
using Xunit;

namespace JobAppManager.Data.Tests;

/// <summary>Main-menu aggregates. Seeds relative to today, since the windows are relative too.
///
/// Both the seed dates and the repository read the same <see cref="FixedClock"/>. Previously the
/// class captured "today" in its constructor while the repository read <c>DateTime.Now</c> inside
/// the call, so a run that straddled local midnight seeded one day and asserted against another.
/// The clock also pins the local timezone, so the windows do not shift with the machine's.</summary>
public class StatisticsTests : IDisposable
{
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 5, 20, 14, 0, 0, TimeSpan.Zero));
    private readonly SqliteTestFixture _fixture;
    private readonly DateOnly _today;

    public StatisticsTests()
    {
        _fixture = SqliteTestFixture.WithClock(_clock);
        _today = _clock.Today;
    }

    public void Dispose() => _fixture.Dispose();

    /// <summary>A repository sharing this test's clock, so its "last N days" windows line up with
    /// the dates the test seeded.</summary>
    private ApplicationRepository RepositoryOver(JobAppContext context) => new(context, _clock);

    [Fact]
    public async Task Statistics_OnEmptyDatabase_AreZeroedNotNull()
    {
        await using var context = _fixture.CreateContext();

        var stats = await RepositoryOver(context).GetStatisticsAsync();

        Assert.Equal(0, stats.TotalApplications);
        Assert.Equal(0, stats.AppliedLast7Days);
        Assert.Equal(0, stats.AppliedLast30Days);
        Assert.Empty(stats.DailyCounts);

        // Every enum member must still be present, so the UI never checks for a missing key.
        // The literal 3 is deliberate: the self-adjusting Length check alone would keep passing
        // if the pipeline were quietly narrowed again.
        Assert.Equal(3, Enum.GetValues<ApplicationStatus>().Length);
        Assert.Equal(Enum.GetValues<ApplicationStatus>().Length, stats.CountByStatus.Count);
        Assert.All(stats.CountByStatus.Values, count => Assert.Equal(0, count));
        Assert.Equal(Enum.GetValues<InterestLevel>().Length, stats.CountByInterest.Count);
        Assert.All(stats.CountByInterest.Values, count => Assert.Equal(0, count));
    }

    [Fact]
    public async Task Statistics_CountByStatusAndInterest_MatchSeededData()
    {
        await SeedAsync(
            (_today, ApplicationStatus.Applied, InterestLevel.Green),
            (_today.AddDays(-2), ApplicationStatus.Applied, InterestLevel.Green),
            (_today.AddDays(-3), ApplicationStatus.Interview, InterestLevel.Yellow),
            (_today.AddDays(-45), ApplicationStatus.Rejected, InterestLevel.Red));

        await using var context = _fixture.CreateContext();
        var stats = await RepositoryOver(context).GetStatisticsAsync();

        Assert.Equal(4, stats.TotalApplications);

        Assert.Equal(2, stats.CountByStatus[ApplicationStatus.Applied]);
        Assert.Equal(1, stats.CountByStatus[ApplicationStatus.Interview]);
        Assert.Equal(1, stats.CountByStatus[ApplicationStatus.Rejected]);

        Assert.Equal(2, stats.CountByInterest[InterestLevel.Green]);
        Assert.Equal(1, stats.CountByInterest[InterestLevel.Yellow]);
        Assert.Equal(1, stats.CountByInterest[InterestLevel.Red]);
    }

    [Fact]
    public async Task Statistics_RecentWindows_AreInclusiveOfTodayAndExcludeOlderRows()
    {
        await SeedAsync(
            (_today, ApplicationStatus.Applied, InterestLevel.Green),          // in 7 and 30
            (_today.AddDays(-6), ApplicationStatus.Applied, InterestLevel.Green),  // edge of 7
            (_today.AddDays(-7), ApplicationStatus.Applied, InterestLevel.Green),  // just outside 7
            (_today.AddDays(-29), ApplicationStatus.Applied, InterestLevel.Green), // edge of 30
            (_today.AddDays(-30), ApplicationStatus.Applied, InterestLevel.Green)); // outside 30

        await using var context = _fixture.CreateContext();
        var stats = await RepositoryOver(context).GetStatisticsAsync();

        Assert.Equal(2, stats.AppliedLast7Days);
        Assert.Equal(4, stats.AppliedLast30Days);
        Assert.Equal(5, stats.TotalApplications);
    }

    [Fact]
    public async Task Statistics_DailyCounts_AreGroupedAndAscending()
    {
        await SeedAsync(
            (_today, ApplicationStatus.Applied, InterestLevel.Green),
            (_today, ApplicationStatus.Interview, InterestLevel.Yellow),
            (_today.AddDays(-4), ApplicationStatus.Applied, InterestLevel.Red));

        await using var context = _fixture.CreateContext();
        var stats = await RepositoryOver(context).GetStatisticsAsync();

        Assert.Equal(2, stats.DailyCounts.Count);
        Assert.Equal(_today.AddDays(-4), stats.DailyCounts[0].Date);
        Assert.Equal(1, stats.DailyCounts[0].Count);
        Assert.Equal(_today, stats.DailyCounts[1].Date);
        Assert.Equal(2, stats.DailyCounts[1].Count);
    }

    [Fact]
    public async Task Statistics_DailyCountWindow_IsHonoured()
    {
        await SeedAsync(
            (_today, ApplicationStatus.Applied, InterestLevel.Green),
            (_today.AddDays(-10), ApplicationStatus.Applied, InterestLevel.Green));

        await using var context = _fixture.CreateContext();
        var stats = await RepositoryOver(context).GetStatisticsAsync(dailyCountWindowDays: 7);

        var only = Assert.Single(stats.DailyCounts);
        Assert.Equal(_today, only.Date);
        // The older row is outside the daily window but still counted in the total.
        Assert.Equal(2, stats.TotalApplications);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(int.MinValue)]
    public async Task Statistics_DailyCountWindow_IsClampedToAtLeastOneDay(int windowDays)
    {
        await SeedAsync(
            (_today, ApplicationStatus.Applied, InterestLevel.Green),
            (_today.AddDays(-1), ApplicationStatus.Applied, InterestLevel.Green));

        await using var context = _fixture.CreateContext();
        var stats = await RepositoryOver(context).GetStatisticsAsync(dailyCountWindowDays: windowDays);

        // Math.Max(windowDays, 1) means a nonsensical window still asks for today, rather than
        // computing a start date after today and silently returning nothing.
        var only = Assert.Single(stats.DailyCounts);
        Assert.Equal(_today, only.Date);
    }

    [Fact]
    public async Task Statistics_ExcludeApplicationsDatedInTheFuture()
    {
        await SeedAsync(
            (_today, ApplicationStatus.Applied, InterestLevel.Green),
            (_today.AddDays(3), ApplicationStatus.Applied, InterestLevel.Green));

        await using var context = _fixture.CreateContext();
        var stats = await RepositoryOver(context).GetStatisticsAsync();

        // The windows are bounded by today at the top, so a future date - a typo, or a planned
        // application - counts toward the total but not toward "applied recently".
        Assert.Equal(2, stats.TotalApplications);
        Assert.Equal(1, stats.AppliedLast7Days);
        Assert.Equal(1, stats.AppliedLast30Days);
        Assert.Equal(new[] { _today }, stats.DailyCounts.Select(d => d.Date));
    }

    [Fact]
    public async Task Statistics_AreUnaffectedByTheTimeOfDay()
    {
        await SeedAsync((_today, ApplicationStatus.Applied, InterestLevel.Green));

        async Task<int> Last7DaysAsync()
        {
            await using var context = _fixture.CreateContext();
            return (await RepositoryOver(context).GetStatisticsAsync()).AppliedLast7Days;
        }

        var atAfternoon = await Last7DaysAsync();

        // One second before local midnight - the point the old wall-clock version could trip on.
        _clock.Set(new DateTimeOffset(_today.Year, _today.Month, _today.Day, 23, 59, 59, TimeSpan.Zero));
        var atMidnightMinusOne = await Last7DaysAsync();

        Assert.Equal(1, atAfternoon);
        Assert.Equal(1, atMidnightMinusOne);
    }

    private async Task SeedAsync(
        params (DateOnly Date, ApplicationStatus Status, InterestLevel Interest)[] rows)
    {
        await using var context = _fixture.CreateContext();
        var repository = RepositoryOver(context);

        var index = 0;
        foreach (var row in rows)
        {
            await repository.AddAsync(
                TestData.Minimal($"Company {index++}", row.Date, row.Status, row.Interest));
        }
    }
}
