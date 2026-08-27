using System.Windows;
using JobAppManager.App.Services;
using JobAppManager.App.ViewModels;
using JobAppManager.App.Views;
using JobAppManager.Data;
using Microsoft.Extensions.DependencyInjection;

namespace JobAppManager.App;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            // Creates %LOCALAPPDATA%\JobApplicationManager\jobapps.db if it is not there and
            // applies any pending migrations. Everything after this can assume a current schema.
            using (var context = DatabaseInitializer.CreateAndMigrate())
            {
                // Disposed immediately: this call exists for its migration side effect, and the
                // app works through short-lived contexts from here on.
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Job Application Manager could not open its database.\n\n" +
                $"{ex.Message}\n\n" +
                $"The database lives at {DbPathProvider.GetDefaultDatabasePath()}.",
                "Startup failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(1);
            return;
        }

        _services = BuildServices();

        var shell = _services.GetRequiredService<MainViewModel>();
        _services.GetRequiredService<NavigationService>().Attach(shell);

        var window = new MainWindow { DataContext = shell };
        MainWindow = window;
        window.Show();

        _ = shell.StartAsync();
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        // One clock for the whole app, so the dashboard greeting, the "last 7 days" windows, and
        // the stamped timestamps all agree - and so tests can pin them.
        services.AddSingleton(TimeProvider.System);

        // A factory rather than a registered DbContext: each operation opens and closes its own,
        // so nothing accumulates tracked entities or serves a stale read after an edit elsewhere.
        services.AddSingleton<Func<JobAppContext>>(sp =>
        {
            var clock = sp.GetRequiredService<TimeProvider>();
            return () => new JobAppContext(DatabaseInitializer.BuildOptions(), clock);
        });
        services.AddSingleton<RepositoryFactory>();

        services.AddSingleton<NavigationService>();
        services.AddSingleton<INavigationService>(sp => sp.GetRequiredService<NavigationService>());
        services.AddSingleton<IDialogService, DialogService>();

        // Pages are singletons so navigating away and back keeps scroll position, filters, and
        // an in-progress form rather than resetting them.
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<ApplicationsViewModel>();
        services.AddSingleton<ApplicationEditorViewModel>();
        services.AddSingleton<MainViewModel>();

        return services.BuildServiceProvider();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
