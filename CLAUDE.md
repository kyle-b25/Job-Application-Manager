# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

A local Windows 11 job-application tracker with persistent SQLite storage, built to the SRS
(rev 1.0). Three screens: a dashboard (quick-submit box + hunt statistics and graphs), a
sortable/filterable applications list, and an edit page for one application.

**Applications are created in exactly one place: the dashboard quick-submit box.** The editor is
reached only by opening an existing row - there is no "Add New" nav entry and no blank-form mode,
because two front doors for the same thing is what the single box replaced.

**Current state: complete.** WPF on .NET 8, MVVM via CommunityToolkit.Mvvm, charts via
LiveCharts2. All three screens exist and the app ships with no seed data.

## Commands

Run from the repo root. If `dotnet` is not found, the SDK is at `C:\Program Files\dotnet`
(prepend it to `$env:Path`).

```powershell
dotnet build JobApplicationManager.sln
dotnet run --project src\JobAppManager.App
dotnet test JobApplicationManager.sln

# A single test
dotnet test --filter "FullyQualifiedName~PersistenceTests.Application_SurvivesContextRecreation"

# A whole test class
dotnet test --filter "FullyQualifiedName~StatisticsTests"

# One project
dotnet test tests\JobAppManager.App.Tests
```

### Packaging

```powershell
winget install JRSoftware.InnoSetup           # one time
powershell -ExecutionPolicy Bypass -File build\build-installer.ps1
```

Produces `artifacts\JobApplicationManager-Setup-<version>.exe`, a per-user installer
(`%LOCALAPPDATA%\Programs\JobApplicationManager`, no admin) carrying its own .NET 8 runtime.

The version lives in `<Version>` in `Directory.Build.props` and nowhere else —
`build-installer.ps1` parses it out and passes it to Inno as `/DAppVersion`.

Three things about the publish shape are deliberate and should not be "tidied up":

- **A folder publish, not `PublishSingleFile`.** Single-file has to unpack `libSkiaSharp.dll`,
  `libHarfBuzzSharp.dll`, and `e_sqlite3.dll` into `%TEMP%` on every cold start, which costs
  startup time and is the shape corporate antivirus flags. Inno compresses the folder anyway
  — 200 MB published lands as a 59 MB setup.
- **No `PublishTrimmed`.** WPF is not trim-safe, and EF Core's migrations reflect over the model.
- **No `[UninstallDelete]` in the .iss.** The database is in `%LOCALAPPDATA%\JobApplicationManager\`,
  which the installer never creates, so an empty section is exactly what keeps a user's
  applications across an uninstall. The correct behaviour here looks like an omission.

`AppId` in `installer\JobApplicationManager.iss` is the upgrade key — changing that GUID gives
every existing user a second, parallel installation.

### Migrations

`dotnet-ef` 8.0.8 is installed globally. Always pass `--project src\JobAppManager.Data` —
that's where both the `DbContext` and `Migrations/` live.

```powershell
dotnet ef migrations add <Name> --project src\JobAppManager.Data --output-dir Migrations
dotnet ef migrations list --project src\JobAppManager.Data
dotnet ef database update --project src\JobAppManager.Data
```

Design-time commands resolve the connection string through `JobAppContextFactory`, so they act
on the **real** user database, not a test one.

## Architecture

```
src/JobAppManager.Core/   entities, enums, repository interface — NO EF Core dependency
src/JobAppManager.Data/   DbContext, entity configurations, migrations, repository impl
src/JobAppManager.App/    WPF: /ViewModels /Views /Services /Styles /Converters
tests/JobAppManager.Data.Tests/
```

**Core must stay free of EF Core.** That's the point of the project split: the WPF layer binds to
the entities directly and depends on `IApplicationRepository` rather than on `JobAppContext`.
Putting an EF attribute or `using Microsoft.EntityFrameworkCore` in `Core` defeats it — mapping
concerns belong in `Data/Configurations/`, one `IEntityTypeConfiguration<T>` per entity, picked up
automatically by `ApplyConfigurationsFromAssembly`. `App` references `Data` only in
`App.xaml.cs`, to wire the container.

**Data model.** `Application` is the root; `Contact` (people messaged) and `StatusChange` (the
stage history) are child tables with cascade delete, not columns. What was sent to the recruiter is
two booleans on the root — `ResumeSubmitted` and `CoverLetterSubmitted` — not a child table.
Interview rounds are `Status = Interview` plus an `InterviewRound` int, not one status per round,
and `InterviewDate` sits beside it under the same rule.

**The pipeline is a history, not a column.** `ApplicationStatus` is exactly three stages —
Applied → Interview → Rejected — where Rejected is reachable from either of the others.
`Application.Status` is only the latest entry — **always move an application with
`IApplicationRepository.ChangeStatusAsync`**, never by assigning `Status`. Assigning it directly
leaves no `StatusChange` behind, and the interview and rejection rates are computed entirely from
that history.

**`InterviewRound` and `InterviewDate` only exist inside Interview.** `ChangeStatusAsync` nulls
both when moving to any other stage, so a move *into* Interview that also sets them has to re-apply
them after the transition — `ApplicationEditorViewModel.SaveAsync` does exactly that.

**Timestamps are automatic.** `JobAppContext.StampTimestamps` sets `CreatedUtc`/`UpdatedUtc` on
save, explicitly un-modifies `CreatedUtc` on updates, and stamps `StatusChange.ChangedUtc` on
insert. Never assign any of them by hand.

**Time comes from an injected `TimeProvider`.** `JobAppContext`, `ApplicationRepository`,
`RepositoryFactory`, `DashboardViewModel`, and `ApplicationEditorViewModel` all take one, defaulting
to `TimeProvider.System`; the container registers a single instance. Do not reintroduce a direct
`DateTime.Now`/`UtcNow` read — it is the seam that makes the statistics windows, the timestamps, and
the quick-submit date testable.

**Database location:** `%LOCALAPPDATA%\JobApplicationManager\jobapps.db`, resolved by
`DbPathProvider`. Startup path is `DatabaseInitializer.CreateAndMigrate()`, which applies
pending migrations. Use migrations, never `EnsureCreated` — the schema is expected to grow.

**Repository queries.** `QueryAsync(ApplicationFilter)` backs the spreadsheet page: all filters
optional, combined with AND, sorts always tie-broken by `Id` for determinism. Text filters use
`EF.Functions.Like`, not `Contains` — EF translates `Contains` to SQLite's `instr()`, which is
case-sensitive, and a search box should not be. Search terms go through `ToLikePattern`, which
escapes `%`, `_`, and the escape character itself and declares `ESCAPE` on the call — otherwise a
user typing `%` matches every row. `GetStatisticsAsync` aggregates in SQL and
zero-fills every enum member so the UI never handles a missing dictionary key. It deliberately
omits empty days and empty months — the chart layer fills those gaps, because a fabricated zero
and "no data yet" are different statements.

## Tests

```
tests/JobAppManager.TestSupport/   SqliteTestFixture, FixedClock, ApplicationBuilder, TestData
tests/JobAppManager.Data.Tests/    repository, persistence, statistics, status history, migrations
                                  (StageAnalyticsTests covers the history-derived rates)
tests/JobAppManager.App.Tests/     ViewModels, converters, navigation  (net8.0-windows, UseWPF)
```

xUnit against a **file-backed temp SQLite database** (`SqliteTestFixture`), not the in-memory
provider - cascade deletes behave differently there, and "persists through shutdown" can only be
proven by disposing a context and reopening the same file. Most classes construct their own
fixture per test for isolation; `PersistenceTests` shares one via `IClassFixture` and scopes its
assertions by id. `SqliteTestFixture` has exactly one public constructor on purpose - xUnit
refuses a class fixture with more than one, or with optional parameters - so the clock variant is
the static `SqliteTestFixture.WithClock(...)`.

**Use `FixedClock` for anything time-dependent.** Pass it to the fixture and to the repository, and
the "last N days" windows, the stamped timestamps, and the date a quick-submitted application
gets all become exact instead of tolerance-banded. It also pins the local timezone to UTC so results do not move with the
machine. To build an application that sat in a stage for eleven days, advance the clock between
real `ChangeStatusAsync` calls - never insert history rows with raw SQL, which skips the code path
the app actually uses.

**Data-carrying migrations get their own tests.** Every other test starts from an empty database,
which exercises a migration's DDL but never its `Sql()` statements. `MigrationTests` migrates to a
named earlier migration with `IMigrator.MigrateAsync("<id>")`, writes rows in the *old* shape with
raw SQL, then migrates the rest of the way and asserts on what survived. Any future migration that
remaps or backfills belongs there — writing one without a test means the remap is first run on a
user's real data.

**ViewModel tests are integration tests by design.** They run against a real `RepositoryFactory`
over a real SQLite file (`ViewModelTestBase`), because a fake `IApplicationRepository` would have to
re-implement the filtering, sorting, and statistics logic and would then pass while the real SQL was
wrong. Only `INavigationService` and `IDialogService` are substituted, by the hand-written recording
fakes in `Fakes.cs`. `ViewModelTestBase`'s clock is set later than every default seed date, because
the editor refuses to save an application dated in the future.

Package versions live in `Directory.Packages.props`, not in the csproj files. Coverage:
`dotnet test --collect:"XPlat Code Coverage"`.

## UI

Theme lives entirely in `src/JobAppManager.App/Styles/`. `Colors.xaml` is the only file with
literal hex in it; everything else references its keys. Controls are restyled with full
`ControlTemplate`s rather than property setters — WPF's stock chrome survives setters, and that is
the main way an app still looks default.

**Binding gotchas this codebase already worked around:**

- List rows are `ApplicationRowViewModel`, which carries its own `EditCommand`/`DeleteCommand`.
  Do not reach back to the page with a `RelativeSource AncestorType` binding from inside an item
  template — when that resolves to null the buttons still hover and press, and silently do nothing.
- The custom `ComboBox` template binds its selection box to `ItemTemplate`, not
  `SelectionBoxItemTemplate`, so every `ComboBox` sets an explicit `ItemTemplate` rather than
  `DisplayMemberPath`. With `DisplayMemberPath` the selected item renders as its `ToString()`.
- Entities are plain POCOs with no `INotifyPropertyChanged`. The editor copies fields in and out
  of an `ObservableValidator` rather than binding a form straight to an `Application`.
- The custom `ScrollBar` template in `Controls.xaml` is a bare `Track`, which inherits none of the
  stock chrome's safeguards: the thumb needs an explicit `MinHeight`/`MinWidth` or it shrinks to an
  ungrabbable sliver on a long list, and the horizontal trigger has to set `Track.Orientation` as
  well as `IsDirectionReversed`.
- Neither dashboard chart is hoverable — `IsHoverable = false` on the series plus
  `TooltipPosition="Hidden"` on the control, and `HoverPushout = 0` so a pie slice does not slide
  out from under the cursor. The counts are printed on the marks instead.
- `InterestLevel` members are named for their dot colour (`Red`/`Yellow`/`Green`); the labels come
  from `EnumDisplayNameConverter.Humanize`, which maps them to "Low interest" / "Interested" /
  "High interest". Never render an `InterestLevel` with `ToString()`.
