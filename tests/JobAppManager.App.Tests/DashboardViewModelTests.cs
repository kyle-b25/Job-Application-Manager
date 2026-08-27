using JobAppManager.Core.Enums;
using Xunit;

namespace JobAppManager.App.Tests;

/// <summary>The dashboard's numbers, its empty state, and the gap-filling the chart layer owns.
///
/// The chart series are built with LiveCharts types but never rendered here; what matters is the
/// shape of the data handed to them, which is where the interesting logic lives.</summary>
public class DashboardViewModelTests : ViewModelTestBase
{
    [Fact]
    public async Task EmptyDatabase_ShowsTheEmptyStateAndNoCharts()
    {
        var vm = NewDashboardViewModel();
        await vm.ActivateAsync();

        Assert.True(vm.IsEmpty);
        Assert.False(vm.HasData);
        Assert.Null(vm.Subtitle);
        Assert.Empty(vm.StatusSeries);
        Assert.Empty(vm.StageDurationSeries);
        Assert.False(vm.HasStageDurations);
    }

    [Fact]
    public async Task SummaryNumbers_ComeFromTheHistoryNotJustTheCurrentStage()
    {
        var interviewedThenRejected = await SeedAsync(b => b
            .At("Alpha").AppliedOn(2026, 9, 1).WithStatus(ApplicationStatus.Applied));
        var offered = await SeedAsync(b => b
            .At("Beta").AppliedOn(2026, 9, 2).WithStatus(ApplicationStatus.Applied));
        await SeedAsync(b => b
            .At("Gamma").AppliedOn(2026, 9, 3).WithStatus(ApplicationStatus.Applied));
        await SeedAsync(b => b
            .At("Delta").AppliedOn(2026, 9, 4).WithStatus(ApplicationStatus.Wishlist));

        await using (var scope = Repositories.Create())
        {
            Clock.AdvanceDays(3);
            await scope.Repository.ChangeStatusAsync(interviewedThenRejected.Id, ApplicationStatus.Interview);
            await scope.Repository.ChangeStatusAsync(interviewedThenRejected.Id, ApplicationStatus.Rejected);
            await scope.Repository.ChangeStatusAsync(offered.Id, ApplicationStatus.Offer);
        }

        var vm = NewDashboardViewModel();
        await vm.ActivateAsync();

        Assert.True(vm.HasData);
        Assert.Equal(4, vm.TotalApplications);
        Assert.Equal(3, vm.ActiveApplications);   // Rejected is out; Wishlist still counts as active

        // 3 submitted, 2 of them interviewed or better, 1 offered.
        Assert.Equal(2d / 3d, vm.InterviewRate, 6);
        Assert.Equal(1d / 3d, vm.OfferRate, 6);
        Assert.Equal("3 still in play out of 4 tracked.", vm.Subtitle);
    }

    [Fact]
    public async Task StatusChart_DropsStagesWithNothingInThem()
    {
        await SeedAsync(b => b.At("Alpha").AppliedOn(2026, 9, 1).WithStatus(ApplicationStatus.Applied));
        await SeedAsync(b => b.At("Beta").AppliedOn(2026, 9, 2).WithStatus(ApplicationStatus.Applied));
        await SeedAsync(b => b.At("Gamma").AppliedOn(2026, 9, 3).WithStatus(ApplicationStatus.Rejected));

        var vm = NewDashboardViewModel();
        await vm.ActivateAsync();

        // Two occupied stages out of seven: an empty slice is invisible but still takes a legend
        // entry, so the chart only gets the stages that exist.
        Assert.Equal(2, vm.StatusSeries.Length);
        Assert.Equal(
            new[] { "Applied", "Rejected" },
            vm.StatusSeries.Select(s => s.Name));
    }

    [Fact]
    public async Task OverTimeChart_FillsInMonthsThatHadNoApplications()
    {
        await SeedAsync(b => b.At("Alpha").AppliedOn(2026, 6, 3));
        await SeedAsync(b => b.At("Beta").AppliedOn(2026, 9, 15));

        var vm = NewDashboardViewModel();
        await vm.ActivateAsync();

        // The repository returns only June and September. Drawing those side by side would imply
        // they were consecutive, so July and August come back as explicit zeros.
        var axis = Assert.Single(vm.OverTimeXAxes);
        Assert.Equal(new[] { "Jun 26", "Jul 26", "Aug 26", "Sep 26" }, axis.Labels);

        var series = Assert.Single(vm.OverTimeSeries);
        Assert.Equal(new[] { 1, 0, 0, 1 }, ((IEnumerable<int>)series.Values!).ToArray());
    }

    [Fact]
    public async Task OverTimeChart_CapsTheAxisAtTwoYears()
    {
        await SeedAsync(b => b.At("Ancient").AppliedOn(2022, 1, 5));
        await SeedAsync(b => b.At("Recent").AppliedOn(2026, 9, 1));

        var vm = NewDashboardViewModel();
        await vm.ActivateAsync();

        // Left uncapped this would be 57 columns a few pixels wide each.
        var axis = Assert.Single(vm.OverTimeXAxes);
        Assert.Equal(24, axis.Labels!.Count);
        Assert.Equal("Sep 26", axis.Labels[^1]);
    }

    [Fact]
    public async Task StageDurations_AppearOnlyOnceSomethingHasActuallyMoved()
    {
        var seeded = await SeedAsync(b => b
            .At("Alpha").AppliedOn(2026, 9, 1).WithStatus(ApplicationStatus.Applied));

        var vm = NewDashboardViewModel();
        await vm.ActivateAsync();

        // One history entry and no transition: nothing to average, so the card shows a sentence
        // rather than an axis of zeros.
        Assert.False(vm.HasStageDurations);
        Assert.Empty(vm.StageDurationSeries);

        Clock.AdvanceDays(9);
        await using (var scope = Repositories.Create())
        {
            await scope.Repository.ChangeStatusAsync(seeded.Id, ApplicationStatus.PhoneScreen);
        }

        await vm.ActivateAsync();

        Assert.True(vm.HasStageDurations);
        var series = Assert.Single(vm.StageDurationSeries);
        Assert.Equal(new[] { 9d }, ((IEnumerable<double>)series.Values!).ToArray());
        Assert.Equal(new[] { "Applied" }, Assert.Single(vm.StageDurationYAxes).Labels);
    }

    [Theory]
    [InlineData(6, "Good morning")]
    [InlineData(11, "Good morning")]
    [InlineData(12, "Good afternoon")]
    [InlineData(17, "Good afternoon")]
    [InlineData(18, "Good evening")]
    [InlineData(23, "Good evening")]
    public void Greeting_FollowsTheInjectedClock(int hour, string expected)
    {
        Clock.Set(new DateTimeOffset(2026, 9, 15, hour, 0, 0, TimeSpan.Zero));

        var vm = NewDashboardViewModel();

        Assert.Equal(expected, vm.Greeting);
        Assert.Equal(expected, vm.Title);
    }

    [Fact]
    public void AddFirst_NavigatesToTheEditor()
    {
        var vm = NewDashboardViewModel();

        vm.AddFirstCommand.Execute(null);

        Assert.Equal(1, Navigation.NewApplicationCount);
    }

    [Fact]
    public async Task Activate_IsRepeatableWithoutDoublingAnything()
    {
        await SeedAsync(b => b.At("Alpha").AppliedOn(2026, 9, 1));

        var vm = NewDashboardViewModel();
        await vm.ActivateAsync();
        await vm.ActivateAsync();
        await vm.ActivateAsync();

        // The page reloads on every navigation, so the series have to be rebuilt rather than
        // appended to.
        Assert.Equal(1, vm.TotalApplications);
        Assert.Single(vm.StatusSeries);
        Assert.Single(vm.OverTimeSeries);
    }
}
