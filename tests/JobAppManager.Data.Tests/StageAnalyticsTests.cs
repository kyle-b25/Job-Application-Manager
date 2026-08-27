using JobAppManager.Core.Enums;
using JobAppManager.Data.Repositories;
using JobAppManager.TestSupport;
using Xunit;

namespace JobAppManager.Data.Tests;

/// <summary>The history-derived dashboard numbers: active count and the interview/rejection
/// rates, which are read off the status history rather than the current column.
///
/// Every move here is made by advancing a <see cref="FixedClock"/> and calling the real
/// <c>ChangeStatusAsync</c>. Inserting history rows with raw SQL instead would skip the code path
/// the app actually uses, which is the one worth testing.</summary>
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
    public async Task EmptyDatabase_ReportsZeroRates()
    {
        var stats = await StatisticsAsync();

        Assert.Equal(0, stats.TotalApplications);
        Assert.Equal(0, stats.ActiveApplications);

        // Zero rather than NaN: nothing divided by nothing still has to render as "0%".
        Assert.Equal(0d, stats.InterviewRate);
        Assert.Equal(0d, stats.RejectionRate);
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
