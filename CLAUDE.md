# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

A local Windows 11 job-application tracker with persistent SQLite storage, built to the SRS
(rev 1.0). Three planned screens: a main menu (greeting + hunt statistics and graphs), an
add-application page, and a sortable/filterable spreadsheet page.

**Current state: data layer only.** There is no UI yet. WPF on .NET 8 is the chosen UI stack
but no WPF project exists — do not assume one.

## Commands

Run from the repo root. If `dotnet` is not found, the SDK is at `C:\Program Files\dotnet`
(prepend it to `$env:Path`).

```powershell
dotnet build JobApplicationManager.sln
dotnet test JobApplicationManager.sln

# A single test
dotnet test --filter "FullyQualifiedName~PersistenceTests.Application_SurvivesContextRecreation"

# A whole test class
dotnet test --filter "FullyQualifiedName~StatisticsTests"
```

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
tests/JobAppManager.Data.Tests/
```

**Core must stay free of EF Core.** That's the point of the two-project split: the future WPF
layer references only `Core`, binds to the entities directly, and depends on
`IApplicationRepository` rather than on `JobAppContext`. Putting an EF attribute or `using
Microsoft.EntityFrameworkCore` in `Core` defeats it — mapping concerns belong in
`Data/Configurations/`, one `IEntityTypeConfiguration<T>` per entity, picked up automatically
by `ApplyConfigurationsFromAssembly`.

**Data model.** `Application` is the root; `SubmittedItem` (what was sent to the recruiter, by
name) and `Contact` (people messaged) are child tables with cascade delete, not columns,
because the SRS allows an open-ended set of each. Interview rounds are `Status = Interview`
plus an `InterviewRound` int, not one status per round.

**Timestamps are automatic.** `JobAppContext.StampTimestamps` sets `CreatedUtc`/`UpdatedUtc` on
save and explicitly un-modifies `CreatedUtc` on updates. Never assign either by hand.

**Database location:** `%LOCALAPPDATA%\JobApplicationManager\jobapps.db`, resolved by
`DbPathProvider`. Startup path is `DatabaseInitializer.CreateAndMigrate()`, which applies
pending migrations. Use migrations, never `EnsureCreated` — the schema is expected to grow.

**Repository queries.** `QueryAsync(ApplicationFilter)` backs the spreadsheet page: all filters
optional, combined with AND, sorts always tie-broken by `Id` for determinism. Text filters use
`EF.Functions.Like`, not `Contains` — EF translates `Contains` to SQLite's `instr()`, which is
case-sensitive, and a search box should not be. `GetStatisticsAsync` aggregates in SQL and
zero-fills every enum member so the UI never handles a missing dictionary key.

## Tests

xUnit against a **file-backed temp SQLite database** (`SqliteTestFixture`), not the in-memory
provider — cascade deletes behave differently there, and "persists through shutdown" can only
be proven by disposing a context and reopening the same file. `ApplicationRepositoryTests` and
`StatisticsTests` construct their own fixture per test for isolation; `PersistenceTests` shares
one via `IClassFixture` and scopes its assertions by id.
