using JobAppManager.Core.Enums;
using JobAppManager.Data.Repositories;
using Xunit;

namespace JobAppManager.Data.Tests;

/// <summary>Main-menu aggregates. Seeds relative to today, since the windows are relative too.</summary>
public class StatisticsTests : IDisposable
{
    private readonly SqliteTestFixture _fixture = new();
    private readonly DateOnly _today = DateOnly.FromDateTime(DateTime.Now);

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Statistics_OnEmptyDatabase_AreZeroedNotNull()
    {
        await using var context = _fixture.CreateContext();

        var stats = await new ApplicationRepository(context).GetStatisticsAsync();

        Assert.Equal(0, stats.TotalApplications);
        Assert.Equal(0, stats.AppliedLast7Days);
        Assert.Equal(0, stats.AppliedLast30Days);
        Assert.Empty(stats.DailyCounts);

        // Every enum member must still be present, so the UI never checks for a missing key.
        Assert.Equal(Enum.GetValues<ApplicationStatus>().Length, stats.CountByStatus.Count);
        Assert.All(stats.CountByStatus.Values, count => Assert.Equal(0, count));
        Assert.Equal(Enum.GetValues<InterestLevel>().Length, stats.CountByInterest.Count);
        Assert.All(stats.CountByInterest.Values, count => Assert.Equal(0, count));
    }

    [Fact]
    public async Task Statistics_CountByStatusAndInterest_MatchSeededData()
    {
        await SeedAsync(
            (_today, ApplicationStatus.NoResponse, InterestLevel.Green),
            (_today.AddDays(-2), ApplicationStatus.NoResponse, InterestLevel.Green),
            (_today.AddDays(-3), ApplicationStatus.Interview, InterestLevel.Yellow),
            (_today.AddDays(-45), ApplicationStatus.Rejected, InterestLevel.Red));

        await using var context = _fixture.CreateContext();
        var stats = await new ApplicationRepository(context).GetStatisticsAsync();

        Assert.Equal(4, stats.TotalApplications);

        Assert.Equal(2, stats.CountByStatus[ApplicationStatus.NoResponse]);
        Assert.Equal(1, stats.CountByStatus[ApplicationStatus.Interview]);
        Assert.Equal(1, stats.CountByStatus[ApplicationStatus.Rejected]);
        Assert.Equal(0, stats.CountByStatus[ApplicationStatus.Accepted]);

        Assert.Equal(2, stats.CountByInterest[InterestLevel.Green]);
        Assert.Equal(1, stats.CountByInterest[InterestLevel.Yellow]);
        Assert.Equal(1, stats.CountByInterest[InterestLevel.Red]);
    }

    [Fact]
    public async Task Statistics_RecentWindows_AreInclusiveOfTodayAndExcludeOlderRows()
    {
        await SeedAsync(
            (_today, ApplicationStatus.NoResponse, InterestLevel.Green),          // in 7 and 30
            (_today.AddDays(-6), ApplicationStatus.NoResponse, InterestLevel.Green),  // edge of 7
            (_today.AddDays(-7), ApplicationStatus.NoResponse, InterestLevel.Green),  // just outside 7
            (_today.AddDays(-29), ApplicationStatus.NoResponse, InterestLevel.Green), // edge of 30
            (_today.AddDays(-30), ApplicationStatus.NoResponse, InterestLevel.Green)); // outside 30

        await using var context = _fixture.CreateContext();
        var stats = await new ApplicationRepository(context).GetStatisticsAsync();

        Assert.Equal(2, stats.AppliedLast7Days);
        Assert.Equal(4, stats.AppliedLast30Days);
        Assert.Equal(5, stats.TotalApplications);
    }

    [Fact]
    public async Task Statistics_DailyCounts_AreGroupedAndAscending()
    {
        await SeedAsync(
            (_today, ApplicationStatus.NoResponse, InterestLevel.Green),
            (_today, ApplicationStatus.Interview, InterestLevel.Yellow),
            (_today.AddDays(-4), ApplicationStatus.NoResponse, InterestLevel.Red));

        await using var context = _fixture.CreateContext();
        var stats = await new ApplicationRepository(context).GetStatisticsAsync();

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
            (_today, ApplicationStatus.NoResponse, InterestLevel.Green),
            (_today.AddDays(-10), ApplicationStatus.NoResponse, InterestLevel.Green));

        await using var context = _fixture.CreateContext();
        var stats = await new ApplicationRepository(context)
            .GetStatisticsAsync(dailyCountWindowDays: 7);

        var only = Assert.Single(stats.DailyCounts);
        Assert.Equal(_today, only.Date);
        // The older row is outside the daily window but still counted in the total.
        Assert.Equal(2, stats.TotalApplications);
    }

    private async Task SeedAsync(
        params (DateOnly Date, ApplicationStatus Status, InterestLevel Interest)[] rows)
    {
        await using var context = _fixture.CreateContext();
        var repository = new ApplicationRepository(context);

        var index = 0;
        foreach (var row in rows)
        {
            await repository.AddAsync(
                TestData.Minimal($"Company {index++}", row.Date, row.Status, row.Interest));
        }
    }
}
