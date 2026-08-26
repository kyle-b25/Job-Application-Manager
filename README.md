# Job-Application-Manager
A job application manager to run for Windows 11 with persistent local data. This is a personal project meant to streamline my job application process to save myself time and energy.

## Status

Data layer complete: entities, EF Core + SQLite persistence, migrations, and a repository
covering the queries the planned screens need. No UI yet — WPF (.NET 8) is next.

## Requirements

- .NET 8 SDK
- `dotnet tool install --global dotnet-ef --version 8.0.8` (for migrations)

## Build and test

```powershell
dotnet build JobApplicationManager.sln
dotnet test JobApplicationManager.sln
```

The database is created at `%LOCALAPPDATA%\JobApplicationManager\jobapps.db` and migrated on
first run.
