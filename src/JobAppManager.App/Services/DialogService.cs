using System.Diagnostics;
using System.Windows;

namespace JobAppManager.App.Services;

public sealed class DialogService : IDialogService
{
    public bool ConfirmDestructive(string title, string message, string confirmLabel)
    {
        // MessageBox rather than a themed custom window: it is the one surface where matching
        // the OS exactly is better than matching the app, because it is what the user has been
        // trained to read carefully before destroying something.
        var result = MessageBox.Show(
            Application.Current.MainWindow,
            message,
            title,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);

        return result == MessageBoxResult.OK;
    }

    public void ShowError(string title, string message) =>
        MessageBox.Show(
            Application.Current.MainWindow,
            message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
}

/// <summary>Hands a path to the shell, the same way the job-posting hyperlink hands it a URL.</summary>
public sealed class ShellLauncher : IShellLauncher
{
    // UseShellExecute is what makes the OS pick the handler - Explorer for a folder. Without it
    // .NET tries to exec the path as a program and throws.
    public void OpenFolder(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
}
