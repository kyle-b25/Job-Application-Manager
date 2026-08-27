using JobAppManager.TestSupport;
using JobAppManager.Core.Entities;
using JobAppManager.Core.Enums;
using JobAppManager.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobAppManager.Data.Tests;

/// <summary>Status transitions and the history they leave behind. Owns its fixture so each test
/// sees only the rows it wrote.</summary>
public class StatusHistoryTests : IDisposable
{
    private readonly SqliteTestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private async Task<int> AddAsync(ApplicationStatus status)
    {
        await using var context = _fixture.CreateContext();
        var saved = await new ApplicationRepository(context).AddAsync(
            TestData.Minimal("Acme Corp", new DateOnly(2026, 8, 1), status));
        return saved.Id;
    }

    private async Task<IReadOnlyList<StatusChange>> HistoryAsync(int id)
    {
        await using var context = _fixture.CreateContext();
        return await context.StatusChanges
            .AsNoTracking()
            .Where(s => s.ApplicationId == id)
            .OrderBy(s => s.ChangedUtc).ThenBy(s => s.Id)
            .ToListAsync();
    }

    [Fact]
    public async Task Add_SeedsHistoryWithTheStartingStatus()
    {
        var id = await AddAsync(ApplicationStatus.Interview);

        var entry = Assert.Single(await HistoryAsync(id));
        Assert.Equal(ApplicationStatus.Interview, entry.Status);
        Assert.NotEqual(default, entry.ChangedUtc);
    }

    [Fact]
    public async Task ChangeStatus_AppendsExactlyOneEntry()
    {
        var id = await AddAsync(ApplicationStatus.Applied);

        await using (var context = _fixture.CreateContext())
        {
            Assert.True(await new ApplicationRepository(context)
                .ChangeStatusAsync(id, ApplicationStatus.Interview, "Round 1 scheduled"));
        }

        var history = await HistoryAsync(id);
        Assert.Equal(2, history.Count);
        Assert.Equal(ApplicationStatus.Applied, history[0].Status);
        Assert.Equal(ApplicationStatus.Interview, history[1].Status);
        Assert.Equal("Round 1 scheduled", history[1].Note);
    }

    [Fact]
    public async Task ChangeStatus_ToTheSameStatus_AppendsNothing()
    {
        var id = await AddAsync(ApplicationStatus.Applied);

        await using (var context = _fixture.CreateContext())
        {
            Assert.True(await new ApplicationRepository(context)
                .ChangeStatusAsync(id, ApplicationStatus.Applied));
        }

        Assert.Single(await HistoryAsync(id));
    }

    [Fact]
    public async Task ChangeStatus_UpdatesTheCurrentStatus()
    {
        var id = await AddAsync(ApplicationStatus.Applied);

        await using (var context = _fixture.CreateContext())
        {
            await new ApplicationRepository(context).ChangeStatusAsync(id, ApplicationStatus.Rejected);
        }

        await using (var context = _fixture.CreateContext())
        {
            var loaded = await new ApplicationRepository(context).GetByIdAsync(id);
            Assert.Equal(ApplicationStatus.Rejected, loaded!.Status);
            Assert.Equal(2, loaded.StatusHistory.Count);
        }
    }

    [Fact]
    public async Task ChangeStatus_AwayFromInterview_ClearsTheRoundNumberAndTheInterviewDate()
    {
        int id;
        await using (var context = _fixture.CreateContext())
        {
            var saved = await new ApplicationRepository(context).AddAsync(TestData.FullApplication());
            id = saved.Id;   // FullApplication is Interview, round 2, with an interview date
        }

        await using (var context = _fixture.CreateContext())
        {
            await new ApplicationRepository(context).ChangeStatusAsync(id, ApplicationStatus.Rejected);
        }

        await using (var context = _fixture.CreateContext())
        {
            var loaded = await new ApplicationRepository(context).GetByIdAsync(id);
            Assert.Null(loaded!.InterviewRound);
            Assert.Null(loaded.InterviewDate);
        }
    }

    [Fact]
    public async Task ChangeStatus_IntoInterview_KeepsTheRoundNumberTheCallerSet()
    {
        var id = await AddAsync(ApplicationStatus.Applied);

        await using (var context = _fixture.CreateContext())
        {
            var repository = new ApplicationRepository(context);
            await repository.ChangeStatusAsync(id, ApplicationStatus.Interview);

            var loaded = (await repository.GetByIdAsync(id))!;
            loaded.InterviewRound = 2;
            await repository.UpdateAsync(loaded);
        }

        // Only a move *away* from Interview clears the round; a later note or edit must not.
        await using (var context = _fixture.CreateContext())
        {
            var repository = new ApplicationRepository(context);
            await repository.ChangeStatusAsync(id, ApplicationStatus.Interview, "Round 2 booked");

            var loaded = (await repository.GetByIdAsync(id))!;
            Assert.Equal(2, loaded.InterviewRound);
        }
    }

    [Fact]
    public async Task ChangeStatus_ToTheSameStatus_LeavesTheRoundNumberAlone()
    {
        int id;

        await using (var context = _fixture.CreateContext())
        {
            var saved = await new ApplicationRepository(context).AddAsync(TestData.FullApplication());
            id = saved.Id;   // FullApplication is Interview, round 2
        }

        await using (var context = _fixture.CreateContext())
        {
            var repository = new ApplicationRepository(context);

            // The early return fires before the round-clearing logic, so a redundant call must
            // not quietly wipe the round number.
            Assert.True(await repository.ChangeStatusAsync(id, ApplicationStatus.Interview));

            var loaded = (await repository.GetByIdAsync(id))!;
            Assert.Equal(2, loaded.InterviewRound);
        }
    }

    [Fact]
    public async Task ChangeStatus_WithoutANote_StoresNull()
    {
        var id = await AddAsync(ApplicationStatus.Applied);

        await using (var context = _fixture.CreateContext())
        {
            await new ApplicationRepository(context)
                .ChangeStatusAsync(id, ApplicationStatus.Interview);
        }

        var history = await HistoryAsync(id);

        // Null, not empty string: the timeline hides the note row entirely when there isn't one.
        Assert.Null(history[^1].Note);
    }

    [Fact]
    public async Task ChangeStatus_OnAMissingApplication_ReturnsFalse()
    {
        await using var context = _fixture.CreateContext();
        Assert.False(await new ApplicationRepository(context)
            .ChangeStatusAsync(4242, ApplicationStatus.Rejected));
    }

    [Fact]
    public async Task Delete_CascadesToStatusHistory()
    {
        var id = await AddAsync(ApplicationStatus.Applied);

        await using (var context = _fixture.CreateContext())
        {
            var repository = new ApplicationRepository(context);
            await repository.ChangeStatusAsync(id, ApplicationStatus.Interview);
            Assert.True(await repository.DeleteAsync(id));
        }

        Assert.Empty(await HistoryAsync(id));
    }

    [Fact]
    public async Task History_SurvivesContextRecreation()
    {
        var id = await AddAsync(ApplicationStatus.Applied);

        await using (var context = _fixture.CreateContext())
        {
            var repository = new ApplicationRepository(context);
            await repository.ChangeStatusAsync(id, ApplicationStatus.Interview);
            await repository.ChangeStatusAsync(id, ApplicationStatus.Rejected);
        }

        // A brand new context over the same file - the only way to prove it reached the disk.
        await using (var context = _fixture.CreateContext())
        {
            var loaded = await new ApplicationRepository(context).GetByIdAsync(id);

            Assert.Equal(
                new[]
                {
                    ApplicationStatus.Applied,
                    ApplicationStatus.Interview,
                    ApplicationStatus.Rejected
                },
                loaded!.StatusHistory.Select(s => s.Status));
        }
    }
}
