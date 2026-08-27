using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JobAppManager.App.Converters;
using JobAppManager.App.Services;
using JobAppManager.Core.Abstractions;
using JobAppManager.Core.Entities;
using JobAppManager.Core.Enums;

namespace JobAppManager.App.ViewModels;

/// <summary>A choice in the status filter, including the "All" entry that maps to no filter.</summary>
public record StatusFilterOption(ApplicationStatus? Status, string DisplayName);

/// <summary>A choice in the sort dropdown. Direction is folded in so the user picks one thing
/// ("Newest first") rather than a field and a direction separately.</summary>
public record SortOption(ApplicationSortField Field, bool Descending, string DisplayName);

public partial class ApplicationsViewModel : PageViewModel
{
    private readonly RepositoryFactory _repositories;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;

    /// <summary>Cancels the pending debounced search when another keystroke arrives.</summary>
    private CancellationTokenSource? _searchDebounce;

    public ApplicationsViewModel(
        RepositoryFactory repositories,
        INavigationService navigation,
        IDialogService dialogs)
    {
        _repositories = repositories;
        _navigation = navigation;
        _dialogs = dialogs;

        StatusOptions = new[] { new StatusFilterOption(null, "All statuses") }
            .Concat(Enum.GetValues<ApplicationStatus>()
                .Select(s => new StatusFilterOption(s, EnumDisplayNameConverter.Humanize(s))))
            .ToList();

        SortOptions = new List<SortOption>
        {
            new(ApplicationSortField.DateApplied, true, "Newest first"),
            new(ApplicationSortField.DateApplied, false, "Oldest first"),
            new(ApplicationSortField.CompanyName, false, "Company A-Z"),
            new(ApplicationSortField.CompanyName, true, "Company Z-A"),
            new(ApplicationSortField.JobTitle, false, "Job title A-Z"),
            new(ApplicationSortField.Status, false, "Pipeline stage"),
            new(ApplicationSortField.InterestLevel, true, "Most interesting")
        };

        _selectedStatus = StatusOptions[0];
        _selectedSort = SortOptions[0];
    }

    public override string Title => "Applications";

    public override string? Subtitle =>
        TotalCount == 0
            ? null
            : ResultCount == TotalCount
                ? $"{TotalCount} tracked"
                : $"{ResultCount} of {TotalCount} shown";

    public IReadOnlyList<StatusFilterOption> StatusOptions { get; }

    public IReadOnlyList<SortOption> SortOptions { get; }

    public ObservableCollection<ApplicationRowViewModel> Results { get; } = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private StatusFilterOption _selectedStatus;

    [ObservableProperty]
    private SortOption _selectedSort;

    [ObservableProperty]
    private ApplicationRowViewModel? _selectedRow;

    /// <summary>The entity behind the selected row, for the detail pane to bind to.</summary>
    public Application? SelectedApplication => SelectedRow?.Application;

    /// <summary>How many rows the current filter returned.</summary>
    [ObservableProperty]
    private int _resultCount;

    /// <summary>How many applications exist at all, ignoring filters. This is what separates
    /// "you haven't added anything yet" from "your filter matched nothing", which are two very
    /// different messages to show someone.</summary>
    [ObservableProperty]
    private int _totalCount;

    public bool HasAnyApplications => TotalCount > 0;

    public bool ShowEmptyDatabaseState => TotalCount == 0 && !IsBusy;

    public bool ShowNoMatchesState => TotalCount > 0 && ResultCount == 0 && !IsBusy;

    public override Task ActivateAsync() => RefreshAsync();

    partial void OnSelectedRowChanged(ApplicationRowViewModel? value) =>
        OnPropertyChanged(nameof(SelectedApplication));

    partial void OnResultCountChanged(int value) => NotifyStateChanged();

    partial void OnTotalCountChanged(int value) => NotifyStateChanged();

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(HasAnyApplications));
        OnPropertyChanged(nameof(ShowEmptyDatabaseState));
        OnPropertyChanged(nameof(ShowNoMatchesState));
        OnPropertyChanged(nameof(Subtitle));
    }

    partial void OnSelectedStatusChanged(StatusFilterOption value) => _ = RefreshAsync();

    partial void OnSelectedSortChanged(SortOption value) => _ = RefreshAsync();

    partial void OnSearchTextChanged(string value) => _ = DebouncedRefreshAsync();

    /// <summary>Waits out the user's typing before querying. Without this every keystroke is a
    /// round trip, and the results flicker through partial matches on the way to the real one.</summary>
    private async Task DebouncedRefreshAsync()
    {
        _searchDebounce?.Cancel();
        _searchDebounce?.Dispose();

        var cts = new CancellationTokenSource();
        _searchDebounce = cts;

        try
        {
            await Task.Delay(250, cts.Token);
            await RefreshAsync();
        }
        catch (OperationCanceledException)
        {
            // Superseded by a later keystroke; the newer call owns the refresh.
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;

        try
        {
            var filter = new ApplicationFilter
            {
                TextContains = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
                Status = SelectedStatus.Status,
                SortBy = SelectedSort.Field,
                SortDescending = SelectedSort.Descending
            };

            await using var scope = _repositories.Create();

            var results = await scope.Repository.QueryAsync(filter);

            // An unfiltered query is already the total, so only pay for a second one when a
            // filter is actually narrowing things.
            var total = filter.TextContains is null && filter.Status is null
                ? results.Count
                : (await scope.Repository.QueryAsync(new ApplicationFilter())).Count;

            var previouslySelectedId = SelectedRow?.Id;

            Results.Clear();

            foreach (var application in results)
            {
                Results.Add(new ApplicationRowViewModel(application, this));
            }

            ResultCount = results.Count;
            TotalCount = total;

            // Keep the selection across a refresh where the row is still in the list, so editing
            // or advancing an application does not bounce the detail pane shut.
            SelectedRow = previouslySelectedId is { } id
                ? Results.FirstOrDefault(r => r.Id == id)
                : null;
        }
        finally
        {
            IsBusy = false;
            NotifyStateChanged();
        }
    }

    /// <summary>The empty state's way out: applications are created on the dashboard, so this
    /// page sends the user there rather than offering a second front door.</summary>
    [RelayCommand]
    private void GoToDashboard() => _navigation.GoToDashboard();

    /// <summary>Called by a row's own command; also bound directly by the detail pane.</summary>
    public void Edit(Application? application)
    {
        if (application is not null)
        {
            _navigation.GoToEditApplication(application.Id);
        }
    }

    public async Task DeleteAsync(Application? application)
    {
        if (application is null)
        {
            return;
        }

        var confirmed = _dialogs.ConfirmDestructive(
            "Delete application",
            $"Delete the {application.JobTitle} application at {application.CompanyName}?\n\n" +
            "Its contacts, submitted items, and status history go with it. This cannot be undone.",
            "Delete");

        if (!confirmed)
        {
            return;
        }

        await using (var scope = _repositories.Create())
        {
            await scope.Repository.DeleteAsync(application.Id);
        }

        SelectedRow = null;
        await RefreshAsync();
    }

    /// <summary>Back to the unfiltered, default-sorted list - the sort included, or "clear"
    /// would leave the list in an order the user did not ask for and cannot see a reason for.</summary>
    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        SelectedStatus = StatusOptions[0];
        SelectedSort = SortOptions[0];
    }
}
