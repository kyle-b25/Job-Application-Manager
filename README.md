# Job Application Manager

A local Windows 11 desktop app for tracking a job hunt: what you applied to, where each
application stands, and what the numbers say about how it is going. Everything lives in a SQLite
file on your own machine — no account, no sync, no network calls.

- **Dashboard** — a quick-submit box that logs an application in four fields, plus the
  analytics: total/active counts, interview and rejection rates, applications over time, a
  breakdown by stage, and the average days spent in each stage.
- **Applications** — searchable, filterable, sortable list with a detail pane and full CRUD.
- **Add New / Edit** — one form for the role, the pipeline stage, what you sent, who you talked
  to, and a timeline of every stage change.

Dark theme with a green accent throughout.

## Requirements

- Windows 10/11
- .NET 8 SDK. If `dotnet` is not on `PATH`, the SDK is at `C:\Program Files\dotnet` — prepend it:
  `$env:Path = "C:\Program Files\dotnet;$env:Path"`
- For migrations only: `dotnet tool install --global dotnet-ef --version 8.0.8`

## Build and run

From the repo root:

```powershell
dotnet build JobApplicationManager.sln
dotnet run --project src\JobAppManager.App
dotnet test JobApplicationManager.sln
```

Running a single test, a whole class, or one project:

```powershell
dotnet test --filter "FullyQualifiedName~PersistenceTests.Application_SurvivesContextRecreation"
dotnet test --filter "FullyQualifiedName~StatusHistoryTests"
dotnet test tests\JobAppManager.App.Tests
```

With a coverage report:

```powershell
dotnet test --collect:"XPlat Code Coverage"
```

### Test layout

```
tests/JobAppManager.TestSupport/   shared fixture, fixed clock, and builders
tests/JobAppManager.Data.Tests/    repository, persistence, statistics, status history
tests/JobAppManager.App.Tests/     ViewModels, converters, navigation
```

Tests run against a throwaway file-backed SQLite database rather than the in-memory provider, and
anything time-dependent uses a `FixedClock` so results do not shift with the machine's clock or
timezone. The ViewModel tests deliberately go through the real repository — only the navigation
and dialog boundaries are faked.

## Packaging

To produce an installer you can hand to someone else:

```powershell
winget install JRSoftware.InnoSetup           # one time
powershell -ExecutionPolicy Bypass -File build\build-installer.ps1
```

That writes `artifacts\JobApplicationManager-Setup-<version>.exe` — a single file you can email or
drop in a shared folder. Recipients need **nothing installed**: the app is published
self-contained, so it carries its own .NET 8 runtime.

The version comes from `<Version>` in `Directory.Build.props`. Bump it there and nowhere else;
`build-installer.ps1` reads it and passes it to the Inno Setup script.

What the installer does on the target machine:

- Installs **per user** to `%LOCALAPPDATA%\Programs\JobApplicationManager` — no admin rights, no
  UAC prompt.
- Adds a Start Menu entry, and a desktop shortcut if the wizard checkbox is ticked.
- Registers an uninstaller in Add/Remove Programs.
- **Uninstalling keeps your data.** The database lives in `%LOCALAPPDATA%\JobApplicationManager\`,
  which the installer never touches, so reinstalling picks your applications back up.

Re-running a newer installer upgrades in place rather than adding a second copy. `build\publish.ps1`
does the publish step on its own if you just want the app folder without an installer.

### A standalone exe, no installer

```powershell
powershell -ExecutionPolicy Bypass -File build\publish.ps1 -SingleFile -OutputDir <somewhere>
```

Produces one ~180 MB `JobApplicationManager.exe` that runs from anywhere with nothing installed —
handy for a copy kept on the desktop or a USB stick. It costs a slower cold start than the
installed build, because the Skia, HarfBuzz and SQLite native DLLs are unpacked to `%TEMP%` on
every launch. It reads the same database as an installed copy, so the two stay in sync.

## Data

The database is created at `%LOCALAPPDATA%\JobApplicationManager\jobapps.db` on first run, and
pending migrations are applied at startup. The app ships with **no seed data** — the first launch
shows an empty state on both the dashboard and the list.

To start over, close the app and delete that file; it will be recreated on the next launch.

### Migrations

`DbContext` and `Migrations/` both live in `JobAppManager.Data`, so every command needs
`--project src\JobAppManager.Data`:

```powershell
dotnet ef migrations add <Name> --project src\JobAppManager.Data --output-dir Migrations
dotnet ef migrations list --project src\JobAppManager.Data
dotnet ef database update --project src\JobAppManager.Data
```

Design-time commands resolve their connection string through `JobAppContextFactory`, which points
at the **real** user database — not a test one.

## Architecture

```
src/JobAppManager.Core/   entities, enums, repository interface   - no EF Core dependency
src/JobAppManager.Data/   DbContext, configurations, migrations, repository
src/JobAppManager.App/    WPF UI (MVVM)
tests/JobAppManager.Data.Tests/
```

`Core` deliberately has no EF Core reference. That is what lets the UI bind to the entities and
depend on `IApplicationRepository` without dragging the persistence stack along; mapping concerns
stay in `Data/Configurations/`, one `IEntityTypeConfiguration<T>` per entity.

The app layer follows MVVM with `CommunityToolkit.Mvvm`: `/ViewModels`, `/Views`, `/Services`,
`/Styles`, `/Converters`. `App.xaml.cs` is the composition root. ViewModels never touch
`JobAppContext` — they take a `RepositoryFactory` and open a short-lived repository per operation,
which keeps a long-running window from accumulating tracked entities and serving stale reads.

Two rules worth knowing before changing anything:

- **Timestamps are automatic.** `JobAppContext.StampTimestamps` owns `CreatedUtc`, `UpdatedUtc`,
  and a status change's `ChangedUtc`. Never assign them by hand.
- **Move stages through `ChangeStatusAsync`.** Assigning `Application.Status` directly updates the
  current stage but leaves no history behind, which silently breaks the rates and the
  stage-duration chart.
- **Read the clock through the injected `TimeProvider`,** not `DateTime.Now`. It is what makes the
  statistics windows, the timestamps, and the date a quick-submitted application gets testable.

Charts use LiveCharts2 (`LiveChartsCore.SkiaSharpView.WPF`).
