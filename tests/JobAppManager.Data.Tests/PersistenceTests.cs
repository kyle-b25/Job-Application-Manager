using JobAppManager.TestSupport;
using JobAppManager.Core.Entities;
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
    public async Task Update_PersistsThroughADetachedGraph()
    {
        int id;

        await using (var context = _fixture.CreateContext())
        {
            var saved = await new ApplicationRepository(context)
                .AddAsync(TestData.Minimal("Detached Co", new DateOnly(2026, 8, 2)));
            id = saved.Id;
        }

        // Loaded on one context and saved through another, which is the shape the UI produces
        // and the only way UpdateAsync's Detached branch is actually reached. Loading and saving
        // on the *same* context leaves the entity tracked and quietly skips that branch.
        Application detached;

        await using (var context = _fixture.CreateContext())
        {
            detached = (await new ApplicationRepository(context).GetByIdAsync(id))!;
        }

        detached.CompanyName = "Detached Co (renamed)";
        detached.Location = "Remote - EU";

        await using (var context = _fixture.CreateContext())
        {
            await new ApplicationRepository(context).UpdateAsync(detached);
        }

        await using (var context = _fixture.CreateContext())
        {
            var reloaded = await context.Applications.FindAsync(id);

            Assert.Equal("Detached Co (renamed)", reloaded!.CompanyName);
            Assert.Equal("Remote - EU", reloaded.Location);
        }
    }

    [Fact]
    public async Task Update_AddsEditsAndRemovesChildrenInOneSave()
    {
        int id;

        await using (var context = _fixture.CreateContext())
        {
            var saved = await new ApplicationRepository(context).AddAsync(
                ApplicationBuilder.An()
                    .At("Children Co")
                    .AppliedOn(2026, 8, 4)
                    .WithContact("Keep Me", "keep@example.com", "Recruiter")
                    .WithContact("Remove Me", "remove@example.com")
                    .Build());

            id = saved.Id;
        }

        int keptContactId;

        await using (var context = _fixture.CreateContext())
        {
            var repository = new ApplicationRepository(context);
            var loaded = (await repository.GetByIdAsync(id))!;

            var kept = loaded.Contacts.Single(c => c.Name == "Keep Me");
            keptContactId = kept.Id;
            kept.Role = "Hiring Manager";

            loaded.Contacts.Remove(loaded.Contacts.Single(c => c.Name == "Remove Me"));
            loaded.Contacts.Add(new Contact { Name = "New Person", Email = "new@example.com" });

            await repository.UpdateAsync(loaded);
        }

        await using (var context = _fixture.CreateContext())
        {
            var reloaded = (await new ApplicationRepository(context).GetByIdAsync(id))!;

            Assert.Equal(2, reloaded.Contacts.Count);

            // The edited row keeps its identity rather than being deleted and reinserted - which
            // is what the editor's SyncChildren relies on to avoid churning ids on every save.
            var kept = reloaded.Contacts.Single(c => c.Name == "Keep Me");
            Assert.Equal(keptContactId, kept.Id);
            Assert.Equal("Hiring Manager", kept.Role);

            Assert.Contains(reloaded.Contacts, c => c.Name == "New Person");
            Assert.DoesNotContain(reloaded.Contacts, c => c.Name == "Remove Me");
        }
    }
}
