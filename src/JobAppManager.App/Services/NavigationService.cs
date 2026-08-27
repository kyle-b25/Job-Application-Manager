using JobAppManager.App.ViewModels;

namespace JobAppManager.App.Services;

/// <summary>Breaks the cycle between the shell and its pages.
///
/// MainViewModel owns navigation, but every page needs to ask for it, and the pages are
/// constructor arguments to MainViewModel - so neither can be built first. This is registered as
/// the INavigationService the pages depend on, and has its target attached once the shell exists.</summary>
public sealed class NavigationService : INavigationService
{
    private MainViewModel? _shell;

    public void Attach(MainViewModel shell) => _shell = shell;

    private MainViewModel Shell => _shell
        ?? throw new InvalidOperationException(
            "Navigation was used before the shell was attached; check the startup order in App.xaml.cs.");

    public void GoToDashboard() => Shell.GoToDashboard();

    public void GoToApplications() => Shell.GoToApplications();

    public void GoToEditApplication(int applicationId) => Shell.GoToEditApplication(applicationId);
}
