using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace JobAppManager.App.Views;

public partial class ApplicationsView : UserControl
{
    public ApplicationsView() => InitializeComponent();

    /// <summary>Hyperlink does not open anything on its own in WPF; the host has to launch it.
    /// UseShellExecute is what hands the URL to the default browser rather than trying to run it.</summary>
    private void OnOpenLink(object sender, RequestNavigateEventArgs e)
    {
        var url = e.Uri.AbsoluteUri;

        if (e.Uri.Scheme is not ("http" or "https"))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        e.Handled = true;
    }
}
