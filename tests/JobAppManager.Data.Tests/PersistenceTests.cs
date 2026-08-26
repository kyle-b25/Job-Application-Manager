using JobAppManager.Core.Enums;
using JobAppManager.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobAppManager.Data.Tests;

public class PersistenceTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _fixture;

    public PersistenceTests(SqliteTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Application_SurvivesContextRecreation()
    {
        int id;

        // First "run" of the app.
        await using (var context = _fixture.CreateContext())
        {
            var repository = new ApplicationRepository(context);
            var saved = await repository.AddAsync(TestData.FullApplication());
            id = saved.Id;
        }

        Assert.NotEqual(0, id);

        // Second "run": a brand new context over the same file.
        await using (var context = _fixture.CreateContext())
        {
            var repository = new ApplicationRepository(context);
            var loaded = await repository.GetByIdAsync(id);

            Assert.NotNull(loaded);
            Assert.Equal("Acme Corp", loaded!.CompanyName);
            Assert.Equal("Software Engineer", loaded.JobTitle);
            Assert.Equal("Remote - US", loaded.Location);
            Assert.Equal(new DateOnly(2026, 8, 20), loaded.DateApplied);
            Assert.Equal(InterestLevel.Green, loaded.InterestLevel);
            Assert.Equal(ApplicationStatus.Interview, loaded.Status);
            Assert.Equal(2, loaded.InterviewRound);
            Assert.True(loaded.FromJobFair);
            Assert.Equal("Met the hiring manager at the fall career fair.", loaded.Notes);

            Assert.Equal(2, loaded.SubmittedItems.Count);
            Assert.Contains(loaded.SubmittedItems,
                s => s.Kind == SubmissionKind.Resume && s.Name == "Resume - Backend v3");
            Assert.Contains(loaded.SubmittedItems,
                s => s.Kind == SubmissionKind.Assessment
                     && s.SubmittedOn == new DateOnly(2026, 8, 22));

            var contact = Assert.Single(loaded.Contacts);
            Assert.Equal("dana.reed@acme.example", contact.Email);
            Assert.Equal("Recruiter", contact.Role);
            Assert.Equal(new DateOnly(2026, 8, 21), contact.DateContacted);
        }
    }

    [Fact]
    public async Task Delete_CascadesToChildren()
    {
        int id;

        await using (var context = _fixture.CreateContext())
        {
            var saved = await new ApplicationRepository(context)
                .AddAsync(TestData.FullApplication("Cascade Co"));
            id = saved.Id;
        }

        await using (var context = _fixture.CreateContext())
        {
            Assert.True(await new ApplicationRepository(context).DeleteAsync(id));
        }

        await using (var context = _fixture.CreateContext())
        {
            Assert.Null(await context.Applications.FindAsync(id));
            Assert.Empty(await context.SubmittedItems.Where(s => s.ApplicationId == id).ToListAsync());
            Assert.Empty(await context.Contacts.Where(c => c.ApplicationId == id).ToListAsync());
        }
    }

    [Fact]
    public async Task Delete_ReturnsFalse_WhenApplicationMissing()
    {
        await using var context = _fixture.CreateContext();

        Assert.False(await new ApplicationRepository(context).DeleteAsync(-1));
    }

    [Fact]
    public async Task Add_StampsCreatedAndUpdatedTimestamps()
    {
        await using var context = _fixture.CreateContext();
        var before = DateTime.UtcNow.AddSeconds(-1);

        var saved = await new ApplicationRepository(context)
            .AddAsync(TestData.Minimal("Stamp Co", new DateOnly(2026, 8, 1)));

        Assert.InRange(saved.CreatedUtc, before, DateTime.UtcNow.AddSeconds(1));
        Assert.Equal(saved.CreatedUtc, saved.UpdatedUtc);
    }

    [Fact]
    public async Task Update_AdvancesUpdatedUtc_AndPreservesCreatedUtc()
    {
        int id;
        DateTime createdUtc;

        await using (var context = _fixture.CreateContext())
        {
            var saved = await new ApplicationRepository(context)
                .AddAsync(TestData.Minimal("Update Co", new DateOnly(2026, 8, 2)));
            id = saved.Id;
            createdUtc = saved.CreatedUtc;
        }

        // A detached graph, the way a UI layer would hand one back.
        await using (var context = _fixture.CreateContext())
        {
            var repository = new ApplicationRepository(context);
            var loaded = await repository.GetByIdAsync(id);
            loaded!.Status = ApplicationStatus.Rejected;
            await repository.UpdateAsync(loaded);
        }

        await using (var context = _fixture.CreateContext())
        {
            var reloaded = await context.Applications.FindAsync(id);

            Assert.Equal(ApplicationStatus.Rejected, reloaded!.Status);
            Assert.Equal(createdUtc, reloaded.CreatedUtc);
            Assert.True(reloaded.UpdatedUtc >= createdUtc);
        }
    }
}
