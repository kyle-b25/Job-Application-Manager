using JobAppManager.App.ViewModels;

namespace JobAppManager.App.Services;

/// <summary>How a page asks to move somewhere else. Implemented by <c>MainViewModel</c>, but
/// pages depend on this rather than on the shell so nothing has a circular reference back to it.</summary>
public interface INavigationService
{
    void GoToDashboard();

    void GoToApplications();

    /// <summary>Opens the editor on an existing application. Applications are only ever created
    /// from the dashboard's quick-submit box, so this is the editor's single entry point.</summary>
    void GoToEditApplication(int applicationId);
}

/// <summary>Opening things in the user's shell - Explorer, the browser. Behind an interface so
/// ViewModels stay testable and a test run does not spawn Explorer windows.</summary>
public interface IShellLauncher
{
    /// <summary>Opens a folder in the system file browser.</summary>
    void OpenFolder(string path);
}

/// <summary>Modal prompts, behind an interface so ViewModels stay testable and free of
/// <c>System.Windows.MessageBox</c>.</summary>
public interface IDialogService
{
    bool ConfirmDestructive(string title, string message, string confirmLabel);

    void ShowError(string title, string message);
}
