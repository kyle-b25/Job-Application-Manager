using JobAppManager.App.ViewModels;
using JobAppManager.Core.Abstractions;
using JobAppManager.Core.Enums;
using Xunit;

namespace JobAppManager.App.Tests;

/// <summary>The list page: filtering, sorting, selection, empty states, and the per-row commands.
/// </summary>
public class ApplicationsViewModelTests : ViewModelTestBase
{
    private async Task SeedThreeAsync()
    {
        await SeedAsync(b => b
            .At("Alpha Corp").For("Backend Engineer").In("Austin, TX")
            .AppliedOn(2026, 5, 1).WithStatus(ApplicationStatus.Applied)
            .WithInterest(InterestLevel.Green));

        await SeedAsync(b => b
            .At("Beta LLC").For("Frontend Engineer").In("Boston, MA")
            .AppliedOn(2026, 5, 10).WithStatus(ApplicationStatus.Interview)
            .WithInterest(InterestLevel.Yellow));

        await SeedAsync(b => b
            .At("Gamma Inc").For("Data Engineer").In("Chicago, IL")
            .AppliedOn(2026, 5, 5).WithStatus(ApplicationStatus.Rejected)
            .WithInterest(InterestLevel.Red));
    }

    private static IReadOnlyList<string> Companies(ApplicationsViewModel vm) =>
        vm.Results.Select(r => r.Application.CompanyName).ToList();

    [Fact]
    public async Task Activate_LoadsEverythingNewestFirst()
    {
        await SeedThreeAsync();

        var vm = NewApplicationsViewModel();
        await vm.ActivateAsync();

        Assert.Equal(new[] { "Beta LLC", "Gamma Inc", "Alpha Corp" }, Companies(vm));
        Assert.Equal(3, vm.ResultCount);
        Assert.Equal(3, vm.TotalCount);
        Assert.Equal("3 tracked", vm.Subtitle);
    }

    [Fact]
    public async Task SearchText_FiltersAcrossCompanyAndJobTitle()
    {
        await SeedThreeAsync();

        var vm = NewApplicationsViewModel();
        await vm.ActivateAsync();

        vm.SearchText = "data";
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "Gamma Inc" }, Companies(vm));

        // Total ignores the filter, which is what lets the page tell "no matches" apart from
        // "nothing added yet".
        Assert.Equal(1, vm.ResultCount);
        Assert.Equal(3, vm.TotalCount);
        Assert.Equal("1 of 3 shown", vm.Subtitle);
    }

    [Fact]
    public async Task StatusFilter_NarrowsToOneStage()
    {
        await SeedThreeAsync();

        var vm = NewApplicationsViewModel();
        vm.SelectedStatus = vm.StatusOptions.Single(o => o.Status == ApplicationStatus.Interview);
        await vm.ActivateAsync();

        Assert.Equal(new[] { "Beta LLC" }, Companies(vm));
    }

    [Fact]
    public async Task SortOption_ChangesTheOrder()
    {
        await SeedThreeAsync();

        var vm = NewApplicationsViewModel();
        vm.SelectedSort = vm.SortOptions.Single(o =>
            o.Field == ApplicationSortField.CompanyName && !o.Descending);
        await vm.ActivateAsync();

        Assert.Equal(new[] { "Alpha Corp", "Beta LLC", "Gamma Inc" }, Companies(vm));
    }

    [Fact]
    public async Task ClearFilters_RestoresEverything()
    {
        await SeedThreeAsync();

        var vm = NewApplicationsViewModel();
        vm.SearchText = "gamma";
        vm.SelectedStatus = vm.StatusOptions.Single(o => o.Status == ApplicationStatus.Rejected);
        await vm.ActivateAsync();
        Assert.Single(vm.Results);

        vm.ClearFiltersCommand.Execute(null);
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(3, vm.Results.Count);
        Assert.Equal(string.Empty, vm.SearchText);
        Assert.Null(vm.SelectedStatus.Status);
    }

    [Fact]
    public async Task EmptyStates_AreMutuallyExclusiveAndCorrect()
    {
        var vm = NewApplicationsViewModel();

        // Nothing added at all: the invitation, not the "no matches" note.
        await vm.ActivateAsync();
        Assert.True(vm.ShowEmptyDatabaseState);
        Assert.False(vm.ShowNoMatchesState);
        Assert.False(vm.HasAnyApplications);
        Assert.Null(vm.Subtitle);

        await SeedThreeAsync();

        // Rows present and unfiltered: neither message.
        await vm.ActivateAsync();
        Assert.False(vm.ShowEmptyDatabaseState);
        Assert.False(vm.ShowNoMatchesState);
        Assert.True(vm.HasAnyApplications);

        // Rows present, filter excludes them all: the quieter message, and the filter bar stays.
        vm.SearchText = "nothing matches this";
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.False(vm.ShowEmptyDatabaseState);
        Assert.True(vm.ShowNoMatchesState);
        Assert.True(vm.HasAnyApplications);
    }

    [Fact]
    public async Task Selection_SurvivesARefreshWhenTheRowIsStillThere()
    {
        await SeedThreeAsync();

        var vm = NewApplicationsViewModel();
        await vm.ActivateAsync();

        vm.SelectedRow = vm.Results.Single(r => r.Application.CompanyName == "Gamma Inc");
        var selectedId = vm.SelectedRow.Id;

        await vm.RefreshCommand.ExecuteAsync(null);

        // Rows are rebuilt on every query, so keeping the selection means matching by id -
        // otherwise the detail pane snaps shut every time anything is edited.
        Assert.NotNull(vm.SelectedRow);
        Assert.Equal(selectedId, vm.SelectedRow!.Id);
        Assert.Equal("Gamma Inc", vm.SelectedApplication!.CompanyName);
    }

    [Fact]
    public async Task Selection_ClearsWhenTheRowFallsOutOfTheFilter()
    {
        await SeedThreeAsync();

        var vm = NewApplicationsViewModel();
        await vm.ActivateAsync();
        vm.SelectedRow = vm.Results.Single(r => r.Application.CompanyName == "Gamma Inc");

        vm.SearchText = "alpha";
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Null(vm.SelectedRow);
        Assert.Null(vm.SelectedApplication);
    }

    [Fact]
    public async Task EveryRow_CarriesWorkingEditAndDeleteCommands()
    {
        await SeedThreeAsync();

        var vm = NewApplicationsViewModel();
        await vm.ActivateAsync();

        // The regression test for row buttons that hover and press but do nothing: when the
        // commands were resolved through a RelativeSource ancestor binding they could come back
        // null, and a Button with a null Command is silently inert.
        Assert.All(vm.Results, row =>
        {
            Assert.NotNull(row.EditCommand);
            Assert.NotNull(row.DeleteCommand);
            Assert.True(row.EditCommand.CanExecute(null));
        });

        var target = vm.Results.First();
        target.EditCommand.Execute(null);

        Assert.Equal(new[] { target.Id }, Navigation.EditedIds);
    }

    [Fact]
    public async Task Delete_AsksFirstAndDoesNothingWhenDeclined()
    {
        await SeedThreeAsync();

        var vm = NewApplicationsViewModel();
        await vm.ActivateAsync();

        Dialogs.ConfirmResult = false;
        await vm.Results.First().DeleteCommand.ExecuteAsync(null);

        Assert.True(Dialogs.ConfirmWasShown);
        Assert.Equal(3, vm.Results.Count);
        Assert.Equal(3, (await AllAsync()).Count);
    }

    [Fact]
    public async Task Delete_RemovesTheRowAndRefreshesWhenConfirmed()
    {
        await SeedThreeAsync();

        var vm = NewApplicationsViewModel();
        await vm.ActivateAsync();

        var doomed = vm.Results.Single(r => r.Application.CompanyName == "Beta LLC");
        vm.SelectedRow = doomed;

        Dialogs.ConfirmResult = true;
        await doomed.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "Gamma Inc", "Alpha Corp" }, Companies(vm));
        Assert.Equal(2, vm.TotalCount);
        Assert.Null(vm.SelectedRow);
    }

    [Fact]
    public void AddNew_NavigatesToTheEditor()
    {
        var vm = NewApplicationsViewModel();

        vm.AddNewCommand.Execute(null);

        Assert.Equal(1, Navigation.NewApplicationCount);
    }
}
