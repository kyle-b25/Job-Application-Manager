using JobAppManager.App.Services;
using JobAppManager.App.ViewModels;
using Xunit;

namespace JobAppManager.App.Tests;

/// <summary>The shell's page switching and, more importantly, which sidebar entry it lights.</summary>
public class MainViewModelTests : ViewModelTestBase
{
    private readonly ApplicationsViewModel _applications;
    private readonly ApplicationEditorViewModel _editor;
    private readonly DashboardViewModel _dashboard;
    private readonly MainViewModel _shell;

    public MainViewModelTests()
    {
        _applications = NewApplicationsViewModel();
        _editor = NewEditorViewModel();
        _dashboard = NewDashboardViewModel();
        _shell = new MainViewModel(_dashboard, _applications, _editor);
    }

    [Fact]
    public void StartsOnTheDashboard()
    {
        Assert.Same(_dashboard, _shell.CurrentPage);
        Assert.True(_shell.IsDashboardSelected);
    }

    [Fact]
    public void EachNavigationCommand_SelectsExactlyOneSidebarEntry()
    {
        _shell.GoToApplicationsCommand.Execute(null);
        AssertOnlySelected(applications: true);

        _shell.GoToNewApplicationCommand.Execute(null);
        AssertOnlySelected(editor: true);

        _shell.GoToDashboardCommand.Execute(null);
        AssertOnlySelected(dashboard: true);
    }

    [Fact]
    public void GoToNewApplication_ShowsTheEditorInNewMode()
    {
        _shell.GoToNewApplication();

        Assert.Same(_editor, _shell.CurrentPage);
        Assert.True(_editor.IsNew);
        Assert.True(_shell.IsEditorSelected);
    }

    [Fact]
    public async Task GoToEditApplication_ShowsTheEditorWithoutLightingAddNew()
    {
        var seeded = await SeedAsync(b => b.At("Editing Co"));

        _shell.GoToEditApplication(seeded.Id);

        // The load is async and completes after the page has already been switched, so this is
        // the wiring that re-raises IsEditorSelected once IsNew flips.
        await WaitUntilAsync(() => !_editor.IsNew);

        Assert.Same(_editor, _shell.CurrentPage);
        Assert.Equal("Editing Co", _editor.CompanyName);

        // "Add New" must stay dark: the user is changing a record, not creating one.
        Assert.False(_shell.IsEditorSelected);
        AssertOnlySelected();
    }

    [Fact]
    public async Task ReturningToAddNew_AfterAnEdit_LightsAddNewAgain()
    {
        var seeded = await SeedAsync(b => b.At("Editing Co"));

        _shell.GoToEditApplication(seeded.Id);
        await WaitUntilAsync(() => !_editor.IsNew);
        Assert.False(_shell.IsEditorSelected);

        _shell.GoToNewApplication();

        Assert.True(_shell.IsEditorSelected);
    }

    [Fact]
    public void SelectionFlags_RaiseChangeNotifications()
    {
        var raised = new List<string?>();
        _shell.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        _shell.GoToApplicationsCommand.Execute(null);

        // Without these the sidebar keeps its old highlight - the flags are computed properties,
        // so nothing notifies for them unless CurrentPage's setter says so.
        Assert.Contains(nameof(MainViewModel.IsDashboardSelected), raised);
        Assert.Contains(nameof(MainViewModel.IsApplicationsSelected), raised);
        Assert.Contains(nameof(MainViewModel.IsEditorSelected), raised);
    }

    private void AssertOnlySelected(
        bool dashboard = false,
        bool applications = false,
        bool editor = false)
    {
        Assert.Equal(dashboard, _shell.IsDashboardSelected);
        Assert.Equal(applications, _shell.IsApplicationsSelected);
        Assert.Equal(editor, _shell.IsEditorSelected);
    }

    /// <summary>Waits for a fire-and-forget load the shell kicked off without awaiting.</summary>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "The awaited condition never became true.");
    }
}

public class NavigationServiceTests
{
    [Fact]
    public void Throws_WhenUsedBeforeTheShellIsAttached()
    {
        var navigation = new NavigationService();

        // A null-reference here would be a mystery at startup; the explicit message names the
        // wiring order in App.xaml.cs as the thing to check.
        var exception = Assert.Throws<InvalidOperationException>(() => navigation.GoToDashboard());

        Assert.Contains("shell was attached", exception.Message);
    }

    [Fact]
    public void DelegatesToTheShell_OnceAttached()
    {
        using var context = new ShellContext();

        context.Navigation.GoToApplications();
        Assert.Same(context.Applications, context.Shell.CurrentPage);

        context.Navigation.GoToNewApplication();
        Assert.Same(context.Editor, context.Shell.CurrentPage);

        context.Navigation.GoToDashboard();
        Assert.Same(context.Dashboard, context.Shell.CurrentPage);
    }

    /// <summary>A real shell behind a real NavigationService, which is the pairing App.xaml.cs
    /// builds and the one place the Attach step could be got wrong.</summary>
    private sealed class ShellContext : ViewModelTestBase
    {
        public ShellContext()
        {
            Dashboard = NewDashboardViewModel();
            Applications = NewApplicationsViewModel();
            Editor = NewEditorViewModel();
            Shell = new MainViewModel(Dashboard, Applications, Editor);
            Navigation = new NavigationService();
            Navigation.Attach(Shell);
        }

        public DashboardViewModel Dashboard { get; }

        public ApplicationsViewModel Applications { get; }

        public ApplicationEditorViewModel Editor { get; }

        public MainViewModel Shell { get; }

        public new NavigationService Navigation { get; }
    }
}
