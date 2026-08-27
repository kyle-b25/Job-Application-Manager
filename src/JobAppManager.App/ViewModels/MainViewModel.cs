using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JobAppManager.App.Services;

namespace JobAppManager.App.ViewModels;

/// <summary>The shell: sidebar selection plus whatever page is in the content area.</summary>
public partial class MainViewModel : ObservableObject, INavigationService
{
    private readonly DashboardViewModel _dashboard;
    private readonly ApplicationsViewModel _applications;
    private readonly ApplicationEditorViewModel _editor;

    public MainViewModel(
        DashboardViewModel dashboard,
        ApplicationsViewModel applications,
        ApplicationEditorViewModel editor)
    {
        _dashboard = dashboard;
        _applications = applications;
        _editor = editor;

        _currentPage = _dashboard;

        // The editor decides whether it is in "new" or "edit" mode asynchronously, after the
        // shell has already switched to it, so the sidebar has to be told to re-check.
        _editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ApplicationEditorViewModel.IsNew))
            {
                OnPropertyChanged(nameof(IsEditorSelected));
            }
        };
    }

    [ObservableProperty]
    private object _currentPage;

    public bool IsDashboardSelected => CurrentPage == _dashboard;

    public bool IsApplicationsSelected => CurrentPage == _applications;

    /// <summary>Only a *new* application lights "Add New". Editing an existing record reached
    /// from the list is not that section, and lighting it there would say the user is about to
    /// create something when they are not.</summary>
    public bool IsEditorSelected => CurrentPage == _editor && _editor.IsNew;

    partial void OnCurrentPageChanged(object value)
    {
        OnPropertyChanged(nameof(IsDashboardSelected));
        OnPropertyChanged(nameof(IsApplicationsSelected));
        OnPropertyChanged(nameof(IsEditorSelected));
    }

    [RelayCommand]
    public void GoToDashboard()
    {
        CurrentPage = _dashboard;
        _ = _dashboard.ActivateAsync();
    }

    [RelayCommand]
    public void GoToApplications()
    {
        CurrentPage = _applications;
        _ = _applications.ActivateAsync();
    }

    [RelayCommand]
    public void GoToNewApplication()
    {
        _editor.LoadNew();
        CurrentPage = _editor;
    }

    public void GoToEditApplication(int applicationId)
    {
        CurrentPage = _editor;
        _ = _editor.LoadAsync(applicationId);
    }

    /// <summary>Loads the landing page once the window is up.</summary>
    public Task StartAsync() => _dashboard.ActivateAsync();
}
