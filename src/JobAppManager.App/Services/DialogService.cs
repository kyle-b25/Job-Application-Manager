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
