using JobAppManager.Core.Enums;
using JobAppManager.Data.Repositories;
using JobAppManager.TestSupport;
using Xunit;

namespace JobAppManager.Data.Tests;

/// <summary>The history-derived dashboard numbers: active count, interview/offer rates, and
/// average days in each stage.
///
/// Every stint here is built by moving a <see cref="FixedClock"/> forward between real
/// <c>ChangeStatusAsync</c> calls. The earlier version inserted history rows with raw SQL to
/// fabricate timestamps, which meant these tests never actually exercised the code path the app
/// uses - and its hand-formatted timestamps would have been wrong under a non-Gregorian calendar.
/// </summary>
public class StageAnalyticsTests : IDisposable
{
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 5, 4, 9, 0, 0, TimeSpan.Zero));
    private readonly SqliteTestFixture _fixture;

    public StageAnalyticsTests() => _fixture = SqliteTestFixture.WithClock(_clock);

    public void Dispose() => _fixture.Dispose();

    private ApplicationRepository RepositoryOver(JobAppContext context) => new(context, _clock);

    /// <summary>Adds an application in <paramref name="startingStatus"/>, which seeds the first
    /// history entry at the clock's current instant.</summary>
    private async Task<int> AddAsync(string company, ApplicationStatus startingStatus)
    {
        await using var context = _fixture.CreateContext();
        var saved = await RepositoryOver(context)
            .AddAsync(TestData.Minimal(company, new DateOnly(2026, 5, 4), startingStatus));

        return saved.Id;
    }

    /// <summary>Advances the clock, then moves the application on - so the stint it just left has
    /// a real, known duration.</summary>
    private async Task MoveAfterAsync(int id, double days, ApplicationStatus to)
    {
        _clock.AdvanceDays(days);

        await using var context = _fixture.CreateContext();
        await RepositoryOver(context).ChangeStatusAsync(id, to);
    }

    private async Task<Core.Abstractions.JobHuntStatistics> StatisticsAsync()
    {
        await using var context = _fixture.CreateContext();
        return await RepositoryOver(context).GetStatisticsAsync();
    }

    [Fact]
    public async Task EmptyDatabase_ReportsZeroRatesAndNoStageDurations()
    {
        var stats = await StatisticsAsync();

        Assert.Equal(0, stats.TotalApplications);
        Assert.Equal(0, stats.ActiveApplications);
        Assert.Equal(0d, stats.InterviewRate);
        Assert.Equal(0d, stats.RejectionRate);
        Assert.Empty(stats.AverageDaysInStage);
        Assert.Empty(stats.MonthlyCounts);
    }

    [Fact]
    public async Task ActiveApplications_ExcludesRejected()
    {
        await AddAsync("Alpha", ApplicationStatus.Applied);
        await AddAsync("Beta", ApplicationStatus.Interview);
        await AddAsync("Gamma", ApplicationStatus.Rejected);
        await AddAsync("Delta", ApplicationStatus.Rejected);
        await AddAsync("Epsilon", ApplicationStatus.Applied);

        var stats = await StatisticsAsync();

        Assert.Equal(5, stats.TotalApplications);
        Assert.Equal(3, stats.ActiveApplications);
    }

    [Fact]
    public async Task Rates_CountApplicationsThatEverReachedAStage_NotJustCurrentOnes()
    {
        var rejectedAfterInterview = await AddAsync("Alpha", ApplicationStatus.Applied);
        await MoveAfterAsync(rejectedAfterInterview, 5, ApplicationStatus.Interview);
        await MoveAfterAsync(rejectedAfterInterview, 4, ApplicationStatus.Rejected);

        await AddAsync("Beta", ApplicationStatus.Applied);

        var interviewing = await AddAsync("Gamma", ApplicationStatus.Applied);
        await MoveAfterAsync(interviewing, 20, ApplicationStatus.Interview);

        var stats = await StatisticsAsync();

        // 3 applications; 2 of them reached Interview, 1 of them reached Rejected. Alpha counts
        // in both - it interviewed and *then* got rejected, and reading the history is the only
        // way to see that from a row whose current status is only the second of those.
        Assert.Equal(2d / 3d, stats.InterviewRate, 6);
        Assert.Equal(1d / 3d, stats.RejectionRate, 6);
    }

    [Fact]
    public async Task Rates_AreZero_WhenNothingHasMovedPastApplied()
    {
        await AddAsync("Alpha", ApplicationStatus.Applied);
        await AddAsync("Beta", ApplicationStatus.Applied);

        var stats = await StatisticsAsync();

        // A real 0-for-2 record this time - the denominator is every application, since there is
        // no longer a stage that means "not sent anywhere yet".
        Assert.Equal(2, stats.TotalApplications);
        Assert.Equal(0d, stats.InterviewRate);
        Assert.Equal(0d, stats.RejectionRate);
    }

    [Fact]
    public async Task AverageDaysInStage_AveragesCompletedStintsOnly()
    {
        var first = await AddAsync("Alpha", ApplicationStatus.Applied);
        var second = await AddAsync("Beta", ApplicationStatus.Applied);

        await MoveAfterAsync(first, 4, ApplicationStatus.Interview);
        await MoveAfterAsync(second, 6, ApplicationStatus.Interview);   // 4 + 6 = 10 days after its own start

        var stats = await StatisticsAsync();

        var applied = Assert.Single(stats.AverageDaysInStage);
        Assert.Equal(ApplicationStatus.Applied, applied.Stage);
        Assert.Equal(7d, applied.AverageDays, 6);
        Assert.Equal(2, applied.SampleSize);

        // Interview is where both currently sit; an unfinished stint is not an average.
        Assert.DoesNotContain(stats.AverageDaysInStage, d => d.Stage == ApplicationStatus.Interview);
    }

    [Fact]
    public async Task AverageDaysInStage_DoesNotPairAcrossApplications()
    {
        await AddAsync("Alpha", ApplicationStatus.Applied);
        _clock.AdvanceDays(30);
        await AddAsync("Beta", ApplicationStatus.Applied);

        var stats = await StatisticsAsync();

        // One history row each and no transitions: the month between them is not a stint.
        Assert.Empty(stats.AverageDaysInStage);
    }

    [Fact]
    public async Task AverageDaysInStage_ReportsEveryCompletedStageInPipelineOrder()
    {
        var id = await AddAsync("Alpha", ApplicationStatus.Applied);

        await MoveAfterAsync(id, 8, ApplicationStatus.Interview);
        await MoveAfterAsync(id, 12, ApplicationStatus.Rejected);

        var stats = await StatisticsAsync();

        Assert.Equal(
            new[]
            {
                (ApplicationStatus.Applied, 8d),
                (ApplicationStatus.Interview, 12d)
            },
            stats.AverageDaysInStage.Select(d => (d.Stage, d.AverageDays)));

        // Rejected is the stage it is sitting in now, so it has no completed stint to average.
        Assert.DoesNotContain(stats.AverageDaysInStage, d => d.Stage == ApplicationStatus.Rejected);
    }

    [Fact]
    public async Task AverageDaysInStage_HandlesTwoMovesAtTheSameInstant()
    {
        var id = await AddAsync("Alpha", ApplicationStatus.Applied);

        await MoveAfterAsync(id, 6, ApplicationStatus.Interview);

        // Same instant: someone advancing two stages in one sitting. Without the ThenBy(Id)
        // tie-break the pairing order would be undefined and the zero-length stint could be
        // attributed to the wrong stage.
        await MoveAfterAsync(id, 0, ApplicationStatus.Rejected);

        var stats = await StatisticsAsync();

        Assert.Equal(
            new[]
            {
                (ApplicationStatus.Applied, 6d),
                (ApplicationStatus.Interview, 0d)
            },
            stats.AverageDaysInStage.Select(d => (d.Stage, d.AverageDays)));
    }

    [Fact]
    public async Task MonthlyCounts_GroupByCalendarMonthAscending()
    {
        await using (var context = _fixture.CreateContext())
        {
            var repository = RepositoryOver(context);
            await repository.AddAsync(TestData.Minimal("Alpha", new DateOnly(2026, 7, 3)));
            await repository.AddAsync(TestData.Minimal("Beta", new DateOnly(2026, 7, 28)));
            await repository.AddAsync(TestData.Minimal("Gamma", new DateOnly(2026, 9, 15)));
        }

        var stats = await StatisticsAsync();

        // August is absent, not zero - the chart layer fills the gap.
        Assert.Equal(
            new[] { (2026, 7, 2), (2026, 9, 1) },
            stats.MonthlyCounts.Select(m => (m.Year, m.Month, m.Count)));
    }
}
