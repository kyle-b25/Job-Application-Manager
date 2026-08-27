using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JobAppManager.App.Services;
using JobAppManager.Data;

namespace JobAppManager.App.ViewModels;

/// <summary>The shell: sidebar selection plus whatever page is in the content area.</summary>
public partial class MainViewModel : ObservableObject, INavigationService
{
    private readonly DashboardViewModel _dashboard;
    private readonly ApplicationsViewModel _applications;
    private readonly ApplicationEditorViewModel _editor;
    private readonly IShellLauncher _shell;

    public MainViewModel(
        DashboardViewModel dashboard,
        ApplicationsViewModel applications,
        ApplicationEditorViewModel editor,
        IShellLauncher shell)
    {
        _dashboard = dashboard;
        _applications = applications;
        _editor = editor;
        _shell = shell;

        _currentPage = _dashboard;
    }

    [ObservableProperty]
    private object _currentPage;

    public bool IsDashboardSelected => CurrentPage == _dashboard;

    public bool IsApplicationsSelected => CurrentPage == _applications;

    /// <summary>The editor is not a sidebar destination - it is only ever reached from a row -
    /// so neither nav entry lights while it is showing.</summary>
    public bool IsEditorSelected => CurrentPage == _editor;

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

    public void GoToEditApplication(int applicationId)
    {
        CurrentPage = _editor;
        _ = _editor.LoadAsync(applicationId);
    }

    /// <summary>Opens the folder holding the SQLite file - the folder, not the file itself, so
    /// the user lands somewhere they can copy or back it up from.</summary>
    [RelayCommand]
    public void OpenDataFolder() =>
        _shell.OpenFolder(Path.GetDirectoryName(DbPathProvider.GetDefaultDatabasePath())!);

    /// <summary>Loads the landing page once the window is up.</summary>
    public Task StartAsync() => _dashboard.ActivateAsync();
}
