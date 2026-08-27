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

        // The over-time series always exists; with no months to draw it is an empty axis rather
        // than a chart of zeros.
        Assert.Empty(Assert.Single(vm.OverTimeXAxes).Labels!);
    }

    [Fact]
    public async Task SummaryNumbers_ComeFromTheHistoryNotJustTheCurrentStage()
    {
        var interviewedThenRejected = await SeedAsync(b => b
            .At("Alpha").AppliedOn(2026, 9, 1).WithStatus(ApplicationStatus.Applied));
        var interviewing = await SeedAsync(b => b
            .At("Beta").AppliedOn(2026, 9, 2).WithStatus(ApplicationStatus.Applied));
        await SeedAsync(b => b
            .At("Gamma").AppliedOn(2026, 9, 3).WithStatus(ApplicationStatus.Applied));
        await SeedAsync(b => b
            .At("Delta").AppliedOn(2026, 9, 4).WithStatus(ApplicationStatus.Applied));

        await using (var scope = Repositories.Create())
        {
            Clock.AdvanceDays(3);
            await scope.Repository.ChangeStatusAsync(interviewedThenRejected.Id, ApplicationStatus.Interview);
            await scope.Repository.ChangeStatusAsync(interviewedThenRejected.Id, ApplicationStatus.Rejected);
            await scope.Repository.ChangeStatusAsync(interviewing.Id, ApplicationStatus.Interview);
        }

        var vm = NewDashboardViewModel();
        await vm.ActivateAsync();

        Assert.True(vm.HasData);
        Assert.Equal(4, vm.TotalApplications);
        Assert.Equal(3, vm.ActiveApplications);   // only the rejected one is out

        // 4 applications; 2 of them reached Interview, 1 reached Rejected. Alpha is in both
        // numerators - it interviewed and then got rejected, and only the history shows that.
        Assert.Equal(2d / 4d, vm.InterviewRate, 6);
        Assert.Equal(1d / 4d, vm.RejectionRate, 6);
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

        // Two occupied stages out of three: an empty slice is invisible but still takes a legend
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

    // ---------------- Quick submit ----------------

    [Fact]
    public async Task QuickSubmit_CreatesAnAppliedApplicationDatedToday()
    {
        var vm = NewDashboardViewModel();
        await vm.ActivateAsync();

        vm.QuickCompany = "  Quick Co  ";
        vm.QuickJobTitle = "  Engineer  ";
        vm.QuickLocation = "  Boston, MA  ";
        vm.QuickInterest = InterestLevel.Green;

        await vm.QuickSubmitCommand.ExecuteAsync(null);

        var saved = Assert.Single(await AllAsync());
        Assert.Equal("Quick Co", saved.CompanyName);
        Assert.Equal("Engineer", saved.JobTitle);
        Assert.Equal("Boston, MA", saved.Location);
        Assert.Equal(InterestLevel.Green, saved.InterestLevel);
        Assert.Equal(ApplicationStatus.Applied, saved.Status);
        Assert.Equal(Clock.Today, saved.DateApplied);

        // Seeded through AddAsync, so it opens its own history - without that entry the row
        // would be invisible to every rate on this page.
        var reloaded = (await LoadAsync(saved.Id))!;
        var entry = Assert.Single(reloaded.StatusHistory);
        Assert.Equal(ApplicationStatus.Applied, entry.Status);

        // Nothing navigates: the point of the box is that you stay on the dashboard.
        Assert.Equal(0, Navigation.ApplicationsCount);
        Assert.Empty(Navigation.EditedIds);
    }

    [Fact]
    public async Task QuickSubmit_IsDisabledUntilTheRequiredFieldsAreFilled()
    {
        var vm = NewDashboardViewModel();
        await vm.ActivateAsync();

        Assert.False(vm.CanQuickSubmit);
        Assert.False(vm.QuickSubmitCommand.CanExecute(null));

        vm.QuickCompany = "Quick Co";
        Assert.False(vm.QuickSubmitCommand.CanExecute(null));

        vm.QuickJobTitle = "Engineer";
        Assert.False(vm.QuickSubmitCommand.CanExecute(null));

        vm.QuickLocation = "Remote";
        Assert.False(vm.QuickSubmitCommand.CanExecute(null));

        // The resume is required too - an application with nothing sent is not one.
        vm.QuickResumeSubmitted = true;
        Assert.True(vm.QuickSubmitCommand.CanExecute(null));

        vm.QuickResumeSubmitted = false;
        Assert.False(vm.QuickSubmitCommand.CanExecute(null));
        vm.QuickResumeSubmitted = true;

        // Whitespace is not a company name; the entity requires a real one.
        vm.QuickCompany = "   ";
        Assert.False(vm.QuickSubmitCommand.CanExecute(null));
    }

    [Fact]
    public async Task QuickSubmit_ClearsTheFormAndRefreshesTheAnalytics()
    {
        var vm = NewDashboardViewModel();
        await vm.ActivateAsync();

        Assert.True(vm.IsEmpty);
        Assert.Equal(0, vm.TotalApplications);

        vm.QuickCompany = "Quick Co";
        vm.QuickJobTitle = "Engineer";
        vm.QuickLocation = "Remote";
        vm.QuickInterest = InterestLevel.Red;
        vm.QuickResumeSubmitted = true;
        vm.QuickCoverLetterSubmitted = true;
        vm.QuickFromJobFair = true;
        vm.QuickContactName = "Dana Reed";
        vm.QuickContactInfo = "dana@quick.co";

        await vm.QuickSubmitCommand.ExecuteAsync(null);

        // The form empties so the next one can be typed straight in.
        Assert.Equal(string.Empty, vm.QuickCompany);
        Assert.Equal(string.Empty, vm.QuickJobTitle);
        Assert.Equal(string.Empty, vm.QuickLocation);
        Assert.Equal(InterestLevel.Yellow, vm.QuickInterest);
        Assert.False(vm.QuickResumeSubmitted);
        Assert.False(vm.QuickCoverLetterSubmitted);
        Assert.False(vm.QuickFromJobFair);
        Assert.Equal(string.Empty, vm.QuickContactName);
        Assert.Equal(string.Empty, vm.QuickContactInfo);

        // And the page recomputes in place rather than waiting for a navigation.
        Assert.Equal(1, vm.TotalApplications);
        Assert.Equal(1, vm.ActiveApplications);
        Assert.Equal(1, vm.AppliedLast7Days);
        Assert.True(vm.HasData);
        Assert.False(vm.IsEmpty);
        Assert.Equal(new[] { "Applied" }, vm.StatusSeries.Select(x => x.Name));
    }

    [Fact]
    public async Task QuickSubmit_StoresTheSubmittedFlagsAndTheJobFairFlag()
    {
        var vm = NewDashboardViewModel();
        await vm.ActivateAsync();

        vm.QuickCompany = "Quick Co";
        vm.QuickJobTitle = "Engineer";
        vm.QuickLocation = "Remote";
        vm.QuickResumeSubmitted = true;
        vm.QuickFromJobFair = true;

        await vm.QuickSubmitCommand.ExecuteAsync(null);

        var saved = Assert.Single(await AllAsync());
        Assert.True(saved.ResumeSubmitted);
        Assert.True(saved.FromJobFair);

        // Left unticked, and so left false rather than quietly defaulted to true.
        Assert.False(saved.CoverLetterSubmitted);
    }

    [Fact]
    public async Task QuickSubmit_StoresTheContactWhenOneIsGiven()
    {
        var vm = NewDashboardViewModel();
        await vm.ActivateAsync();

        vm.QuickCompany = "Quick Co";
        vm.QuickJobTitle = "Engineer";
        vm.QuickLocation = "Remote";
        vm.QuickResumeSubmitted = true;
        vm.QuickContactName = "  Dana Reed  ";
        vm.QuickContactInfo = "  dana@quick.co  ";

        await vm.QuickSubmitCommand.ExecuteAsync(null);

        var saved = Assert.Single(await AllAsync());
        var reloaded = (await LoadAsync(saved.Id))!;

        var contact = Assert.Single(reloaded.Contacts);
        Assert.Equal("Dana Reed", contact.Name);
        Assert.Equal("dana@quick.co", contact.Email);
    }

    [Fact]
    public async Task QuickSubmit_LeavesTheContactsEmptyWhenNoNameIsGiven()
    {
        var vm = NewDashboardViewModel();
        await vm.ActivateAsync();

        vm.QuickCompany = "Quick Co";
        vm.QuickJobTitle = "Engineer";
        vm.QuickLocation = "Remote";
        vm.QuickResumeSubmitted = true;

        await vm.QuickSubmitCommand.ExecuteAsync(null);

        var saved = Assert.Single(await AllAsync());
        Assert.Empty((await LoadAsync(saved.Id))!.Contacts);
    }

    [Fact]
    public async Task QuickSubmit_IsBlockedWhileTheContactIsHalfFilled()
    {
        var vm = NewDashboardViewModel();
        await vm.ActivateAsync();

        vm.QuickCompany = "Quick Co";
        vm.QuickJobTitle = "Engineer";
        vm.QuickLocation = "Remote";
        vm.QuickResumeSubmitted = true;
        Assert.True(vm.QuickSubmitCommand.CanExecute(null));

        // Contact.Email is required by the schema, so a name on its own cannot be saved -
        // and dropping the name the user just typed would be worse than refusing.
        vm.QuickContactName = "Dana Reed";
        Assert.True(vm.QuickContactIncomplete);
        Assert.False(vm.QuickSubmitCommand.CanExecute(null));

        vm.QuickContactInfo = "dana@quick.co";
        Assert.False(vm.QuickContactIncomplete);
        Assert.True(vm.QuickSubmitCommand.CanExecute(null));

        // The other half alone is refused just the same.
        vm.QuickContactName = string.Empty;
        Assert.True(vm.QuickContactIncomplete);
        Assert.False(vm.QuickSubmitCommand.CanExecute(null));
    }

    [Fact]
    public void Title_IsTheStaticPageName()
    {
        Clock.Set(new DateTimeOffset(2026, 9, 15, 6, 0, 0, TimeSpan.Zero));
        Assert.Equal("Dashboard", NewDashboardViewModel().Title);

        // No greeting any more, so the header must not move with the clock.
        Clock.Set(new DateTimeOffset(2026, 9, 15, 22, 0, 0, TimeSpan.Zero));
        Assert.Equal("Dashboard", NewDashboardViewModel().Title);
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
