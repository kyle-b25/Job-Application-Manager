using JobAppManager.App.Services;
using JobAppManager.App.ViewModels;
using JobAppManager.Core.Abstractions;
using JobAppManager.Core.Entities;
using JobAppManager.Data.Repositories;
using JobAppManager.TestSupport;

namespace JobAppManager.App.Tests;

/// <summary>Shared wiring for ViewModel tests.
///
/// These are integration tests on purpose: the ViewModels go through a real
/// <see cref="RepositoryFactory"/> over a real SQLite file rather than a fake repository. A fake
/// would have to re-implement the filtering, sorting, and statistics logic, and would then happily
/// pass while the actual SQL was wrong - which is exactly the class of bug worth catching here.
/// Only the two boundary services the ViewModels talk out through are substituted.</summary>
public abstract class ViewModelTestBase : IDisposable
{
    protected ViewModelTestBase()
    {
        // Deliberately later than every date the seed builders default to. The editor refuses to
        // save an application dated in the future, so a clock set before the seeded DateApplied
        // makes every edit-path test fail on validation for a reason that has nothing to do with
        // what it is testing.
        Clock = new FixedClock(new DateTimeOffset(2026, 9, 15, 14, 0, 0, TimeSpan.Zero));
        Fixture = SqliteTestFixture.WithClock(Clock);
        Repositories = new RepositoryFactory(() => Fixture.CreateContext(), Clock);
        Navigation = new FakeNavigationService();
        Dialogs = new FakeDialogService();
        ShellLauncher = new FakeShellLauncher();
    }

    protected FixedClock Clock { get; }

    protected SqliteTestFixture Fixture { get; }

    protected RepositoryFactory Repositories { get; }

    protected FakeNavigationService Navigation { get; }

    protected FakeDialogService Dialogs { get; }

    protected FakeShellLauncher ShellLauncher { get; }

    protected ApplicationsViewModel NewApplicationsViewModel() =>
        new(Repositories, Navigation, Dialogs);

    protected ApplicationEditorViewModel NewEditorViewModel() =>
        new(Repositories, Navigation, Dialogs, Clock);

    protected DashboardViewModel NewDashboardViewModel() =>
        new(Repositories, Clock);

    /// <summary>Writes an application straight through the repository, bypassing the ViewModels.</summary>
    protected async Task<Application> SeedAsync(Application application)
    {
        await using var scope = Repositories.Create();
        return await scope.Repository.AddAsync(application);
    }

    protected async Task<Application> SeedAsync(Func<ApplicationBuilder, ApplicationBuilder> build) =>
        await SeedAsync(build(ApplicationBuilder.An()).Build());

    /// <summary>Reloads an application with its children, to assert on what was actually saved.</summary>
    protected async Task<Application?> LoadAsync(int id)
    {
        await using var scope = Repositories.Create();
        return await scope.Repository.GetByIdAsync(id);
    }

    /// <summary>Everything in the database, unfiltered.</summary>
    protected async Task<IReadOnlyList<Application>> AllAsync()
    {
        await using var scope = Repositories.Create();
        return await scope.Repository.QueryAsync(new ApplicationFilter());
    }

    public void Dispose() => Fixture.Dispose();
}
