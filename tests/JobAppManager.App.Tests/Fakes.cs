using JobAppManager.App.Services;

namespace JobAppManager.App.Tests;

/// <summary>Records where a ViewModel asked to navigate.
///
/// Hand-written rather than substituted: the assertions here are "which page did it go to, and
/// with which id", which reads better as a recorded field than as a received-call check.</summary>
public sealed class FakeNavigationService : INavigationService
{
    public int DashboardCount { get; private set; }

    public int ApplicationsCount { get; private set; }

    /// <summary>Every id passed to <see cref="GoToEditApplication"/>, in order.</summary>
    public List<int> EditedIds { get; } = new();

    public void GoToDashboard() => DashboardCount++;

    public void GoToApplications() => ApplicationsCount++;

    public void GoToEditApplication(int applicationId) => EditedIds.Add(applicationId);
}

/// <summary>Records the paths a ViewModel asked the shell to open, instead of opening them -
/// a test run must not spawn Explorer windows.</summary>
public sealed class FakeShellLauncher : IShellLauncher
{
    public List<string> OpenedFolders { get; } = new();

    public void OpenFolder(string path) => OpenedFolders.Add(path);
}

/// <summary>A dialog service that never shows anything and answers however the test says.</summary>
public sealed class FakeDialogService : IDialogService
{
    /// <summary>What <see cref="ConfirmDestructive"/> returns. Defaults to false, so a test that
    /// forgets to opt in gets the safe answer rather than a silent delete.</summary>
    public bool ConfirmResult { get; set; }

    public List<string> ConfirmTitles { get; } = new();

    public List<string> ErrorTitles { get; } = new();

    public List<string> ErrorMessages { get; } = new();

    public bool ConfirmWasShown => ConfirmTitles.Count > 0;

    public bool ErrorWasShown => ErrorTitles.Count > 0;

    public bool ConfirmDestructive(string title, string message, string confirmLabel)
    {
        ConfirmTitles.Add(title);
        return ConfirmResult;
    }

    public void ShowError(string title, string message)
    {
        ErrorTitles.Add(title);
        ErrorMessages.Add(message);
    }
}
