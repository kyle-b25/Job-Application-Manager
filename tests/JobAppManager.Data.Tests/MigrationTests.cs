using JobAppManager.Core.Enums;
using JobAppManager.Data;
using JobAppManager.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace JobAppManager.Data.Tests;

/// <summary>The migrations that narrowed the schema carry data with them, and every other test in
/// this suite starts from an empty database - which exercises the DDL but never the remapping SQL.
/// These tests migrate to the last pre-narrowing migration, write rows in the *old* shape, then
/// migrate the rest of the way and assert on what survived. That is the only place the CASE
/// statements, the history dedupe, and the submitted-item backfill are actually run against data.
/// </summary>
public class MigrationTests : IDisposable
{
    /// <summary>The last migration before the pipeline was narrowed: seven statuses, a
    /// SalaryRange column, and a SubmittedItems table.</summary>
    private const string LegacySchema = "20260827033110_AddStatusHistoryAndPipelineFields";

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"jobapps-migration-{Guid.NewGuid():N}.db");

    private JobAppContext CreateContext() =>
        new(DatabaseInitializer.BuildOptions(_databasePath));

    /// <summary>Brings the database up to <paramref name="target"/> and no further.</summary>
    private async Task MigrateToAsync(string target)
    {
        await using var context = CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(target);
    }

    private async Task MigrateToLatestAsync()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync(sql);
    }

    /// <summary>Reads one integer column out of a table the current model may no longer map.</summary>
    private async Task<List<(int Id, int Value)>> ReadIntColumnAsync(string table, string column)
    {
        await using var context = CreateContext();
        await using var command = context.Database.GetDbConnection().CreateCommand();

        await context.Database.OpenConnectionAsync();
        command.CommandText = $"SELECT Id, {column} FROM {table} ORDER BY Id;";

        var rows = new List<(int, int)>();
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetInt32(0), reader.GetInt32(1)));
        }

        return rows;
    }

    [Fact]
    public async Task SimplifyStatusPipeline_MapsEveryOldStatusOntoTheNewThree()
    {
        await MigrateToAsync(LegacySchema);

        // One application per old status, ids 1..7 in old-ordinal order.
        //   0 Wishlist  1 Applied  2 PhoneScreen  3 Interview  4 Offer  5 Rejected  6 Withdrawn
        for (var oldStatus = 0; oldStatus <= 6; oldStatus++)
        {
            await ExecuteAsync(
                "INSERT INTO Applications " +
                "(DateApplied, CompanyName, JobTitle, Location, InterestLevel, Status, " +
                " FromJobFair, CreatedUtc, UpdatedUtc) VALUES " +
                $"('2026-08-20', 'Co{oldStatus}', 'Engineer', 'Boston', 1, {oldStatus}, " +
                "0, '2026-08-20 09:00:00', '2026-08-20 09:00:00');");
        }

        await MigrateToLatestAsync();

        var statuses = await ReadIntColumnAsync("Applications", "Status");

        Assert.Equal(
            new[]
            {
                (int)ApplicationStatus.Applied,     // Wishlist
                (int)ApplicationStatus.Applied,     // Applied
                (int)ApplicationStatus.Applied,     // PhoneScreen
                (int)ApplicationStatus.Interview,   // Interview
                (int)ApplicationStatus.Interview,   // Offer
                (int)ApplicationStatus.Rejected,    // Rejected
                (int)ApplicationStatus.Rejected     // Withdrawn
            },
            statuses.Select(r => r.Value));
    }

    [Fact]
    public async Task SimplifyStatusPipeline_RemapsHistoryAndDropsTheStintsTheCollapseFlattens()
    {
        await MigrateToAsync(LegacySchema);

        await ExecuteAsync(
            "INSERT INTO Applications " +
            "(Id, DateApplied, CompanyName, JobTitle, Location, InterestLevel, Status, " +
            " FromJobFair, CreatedUtc, UpdatedUtc) VALUES " +
            "(1, '2026-08-20', 'Acme', 'Engineer', 'Boston', 1, 5, " +
            "0, '2026-08-20 09:00:00', '2026-08-20 09:00:00');");

        // Wishlist -> Applied -> PhoneScreen -> Interview -> Offer -> Rejected. The first three
        // all collapse onto Applied, and the last two both become Interview then Rejected, so a
        // straight remap would leave three zero-length stints behind.
        var days = 20;

        foreach (var oldStatus in new[] { 0, 1, 2, 3, 4, 5 })
        {
            await ExecuteAsync(
                "INSERT INTO StatusChanges (ApplicationId, Status, ChangedUtc) VALUES " +
                $"(1, {oldStatus}, '2026-08-{days:00} 09:00:00');");
            days++;
        }

        await MigrateToLatestAsync();

        var history = await ReadIntColumnAsync("StatusChanges", "Status");

        // Applied, Interview, Rejected - one entry per stage actually entered, no repeats.
        Assert.Equal(
            new[]
            {
                (int)ApplicationStatus.Applied,
                (int)ApplicationStatus.Interview,
                (int)ApplicationStatus.Rejected
            },
            history.Select(r => r.Value));

        // And the surviving rows are the *first* of each run, so the stage durations measure from
        // when the stage was really entered rather than from the last duplicate.
        Assert.Equal(new[] { 1, 4, 6 }, history.Select(r => r.Id));
    }

    [Fact]
    public async Task SimplifyStatusPipeline_LeavesADistinctHistoryAlone()
    {
        await MigrateToAsync(LegacySchema);

        await ExecuteAsync(
            "INSERT INTO Applications " +
            "(Id, DateApplied, CompanyName, JobTitle, Location, InterestLevel, Status, " +
            " FromJobFair, CreatedUtc, UpdatedUtc) VALUES " +
            "(1, '2026-08-20', 'Acme', 'Engineer', 'Boston', 1, 3, " +
            "0, '2026-08-20 09:00:00', '2026-08-20 09:00:00');");

        // Applied -> Interview: two stages that stay distinct, so nothing may be dropped.
        await ExecuteAsync(
            "INSERT INTO StatusChanges (ApplicationId, Status, ChangedUtc) VALUES " +
            "(1, 1, '2026-08-20 09:00:00'), (1, 3, '2026-08-25 09:00:00');");

        await MigrateToLatestAsync();

        var history = await ReadIntColumnAsync("StatusChanges", "Status");

        Assert.Equal(
            new[] { (int)ApplicationStatus.Applied, (int)ApplicationStatus.Interview },
            history.Select(r => r.Value));
    }

    [Fact]
    public async Task SimplifyStatusPipeline_DoesNotDedupeAcrossApplications()
    {
        await MigrateToAsync(LegacySchema);

        foreach (var id in new[] { 1, 2 })
        {
            await ExecuteAsync(
                "INSERT INTO Applications " +
                "(Id, DateApplied, CompanyName, JobTitle, Location, InterestLevel, Status, " +
                " FromJobFair, CreatedUtc, UpdatedUtc) VALUES " +
                $"({id}, '2026-08-20', 'Co{id}', 'Engineer', 'Boston', 1, 1, " +
                "0, '2026-08-20 09:00:00', '2026-08-20 09:00:00');");

            await ExecuteAsync(
                "INSERT INTO StatusChanges (ApplicationId, Status, ChangedUtc) VALUES " +
                $"({id}, 1, '2026-08-20 09:00:00');");
        }

        await MigrateToLatestAsync();

        // Two applications, each with one Applied entry. Adjacent by id and identical by status,
        // but they belong to different applications and both must survive.
        Assert.Equal(2, (await ReadIntColumnAsync("StatusChanges", "Status")).Count);
    }

    [Fact]
    public async Task SimplifyStatusPipeline_ClearsTheRoundNumberOnAnythingNoLongerInterviewing()
    {
        await MigrateToAsync(LegacySchema);

        // An old PhoneScreen row carrying a round number, and a real Interview row carrying one.
        await ExecuteAsync(
            "INSERT INTO Applications " +
            "(Id, DateApplied, CompanyName, JobTitle, Location, InterestLevel, Status, " +
            " InterviewRound, FromJobFair, CreatedUtc, UpdatedUtc) VALUES " +
            "(1, '2026-08-20', 'Phone Co', 'Engineer', 'Boston', 1, 2, " +
            "3, 0, '2026-08-20 09:00:00', '2026-08-20 09:00:00'), " +
            "(2, '2026-08-20', 'Interview Co', 'Engineer', 'Boston', 1, 3, " +
            "2, 0, '2026-08-20 09:00:00', '2026-08-20 09:00:00');");

        await MigrateToLatestAsync();

        await using var context = CreateContext();
        var repository = new ApplicationRepository(context);

        var phone = (await repository.GetByIdAsync(1))!;
        Assert.Equal(ApplicationStatus.Applied, phone.Status);
        Assert.Null(phone.InterviewRound);

        var interview = (await repository.GetByIdAsync(2))!;
        Assert.Equal(ApplicationStatus.Interview, interview.Status);
        Assert.Equal(2, interview.InterviewRound);
    }

    [Fact]
    public async Task ReplaceSubmittedItemsWithFlags_BackfillsBothFlagsBeforeDroppingTheTable()
    {
        await MigrateToAsync(LegacySchema);

        //  1: a resume only            2: a cover letter only
        //  3: both                     4: an assessment only, which has no flag to land in
        //  5: nothing submitted at all
        for (var id = 1; id <= 5; id++)
        {
            await ExecuteAsync(
                "INSERT INTO Applications " +
                "(Id, DateApplied, CompanyName, JobTitle, Location, InterestLevel, Status, " +
                " FromJobFair, CreatedUtc, UpdatedUtc) VALUES " +
                $"({id}, '2026-08-20', 'Co{id}', 'Engineer', 'Boston', 1, 1, " +
                "0, '2026-08-20 09:00:00', '2026-08-20 09:00:00');");
        }

        // SubmissionKind: 0 Resume, 1 CoverLetter, 2 Assessment.
        await ExecuteAsync(
            "INSERT INTO SubmittedItems (ApplicationId, Kind, Name) VALUES " +
            "(1, 0, 'Resume - Backend v3'), " +
            "(2, 1, 'Cover letter'), " +
            "(3, 0, 'Resume - Backend v3'), (3, 1, 'Cover letter'), " +
            "(4, 2, 'HackerRank screen');");

        await MigrateToLatestAsync();

        await using var context = CreateContext();

        var applications = await context.Applications
            .OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.ResumeSubmitted, a.CoverLetterSubmitted })
            .ToListAsync();

        Assert.Equal(
            new[]
            {
                (1, true, false),
                (2, false, true),
                (3, true, true),
                (4, false, false),   // an assessment is neither, and is dropped with the table
                (5, false, false)
            },
            applications.Select(a => (a.Id, a.ResumeSubmitted, a.CoverLetterSubmitted)));
    }

    [Fact]
    public async Task TheWholeChain_LeavesAWorkingDatabase()
    {
        await MigrateToAsync(LegacySchema);

        await ExecuteAsync(
            "INSERT INTO Applications " +
            "(Id, DateApplied, CompanyName, JobTitle, Location, SalaryRange, InterestLevel, " +
            " Status, FromJobFair, CreatedUtc, UpdatedUtc) VALUES " +
            "(1, '2026-08-20', 'Acme', 'Engineer', 'Boston', '$140k - $170k', 2, 4, " +
            "1, '2026-08-20 09:00:00', '2026-08-20 09:00:00');");

        await ExecuteAsync(
            "INSERT INTO StatusChanges (ApplicationId, Status, ChangedUtc) VALUES " +
            "(1, 1, '2026-08-20 09:00:00'), (1, 4, '2026-08-30 09:00:00');");

        await MigrateToLatestAsync();

        await using var context = CreateContext();
        var repository = new ApplicationRepository(context);

        // Everything the app reads still reads, on a database that started life two schemas ago.
        var loaded = (await repository.GetByIdAsync(1))!;
        Assert.Equal("Acme", loaded.CompanyName);
        Assert.Equal(ApplicationStatus.Interview, loaded.Status);   // was Offer
        Assert.True(loaded.FromJobFair);

        var stats = await repository.GetStatisticsAsync();
        Assert.Equal(1, stats.TotalApplications);
        Assert.Equal(1, stats.ActiveApplications);
        Assert.Equal(1d, stats.InterviewRate);
        Assert.Equal(0d, stats.RejectionRate);

        // Applied -> Interview, ten days apart, and still measurable after the remap.
        var stage = Assert.Single(stats.AverageDaysInStage);
        Assert.Equal(ApplicationStatus.Applied, stage.Stage);
        Assert.Equal(10d, stage.AverageDays);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        foreach (var path in new[] { _databasePath, _databasePath + "-shm", _databasePath + "-wal" })
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // A leaked handle should not fail an otherwise passing test run.
            }
        }
    }
}
