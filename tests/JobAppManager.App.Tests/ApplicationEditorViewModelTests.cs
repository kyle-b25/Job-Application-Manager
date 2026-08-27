using System.ComponentModel.DataAnnotations;
using JobAppManager.App.ViewModels;
using JobAppManager.Core.Enums;
using JobAppManager.TestSupport;
using Xunit;

namespace JobAppManager.App.Tests;

/// <summary>The edit form: validation, loading, child rows, and the rule that pipeline moves go
/// through the repository so history is recorded.
///
/// The form only ever updates - applications are created from the dashboard's quick-submit box -
/// so every test here starts from a row that already exists.</summary>
public class ApplicationEditorViewModelTests : ViewModelTestBase
{
    /// <summary>An editor sitting on a freshly seeded application, which is the only state the
    /// form is ever reached in.</summary>
    private async Task<ApplicationEditorViewModel> NewLoadedEditorAsync(
        Func<ApplicationBuilder, ApplicationBuilder>? build = null)
    {
        var seeded = await SeedAsync(build ?? (b => b
            .At("Northwind Labs").For("Senior Backend Engineer")));

        var vm = NewEditorViewModel();
        await vm.LoadAsync(seeded.Id);
        return vm;
    }

    // ---------------- Validation ----------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://acme.example/careers/1")]
    [InlineData("http://acme.example")]
    public void ValidateJobUrl_AcceptsBlankAndAbsoluteWebLinks(string? url) =>
        Assert.Null(ValidateUrl(url));

    [Theory]
    [InlineData("acme.example/careers")]   // no scheme - the Open button could not launch it
    [InlineData("file:///C:/jobs.txt")]    // absolute, but not something to open in a browser
    [InlineData("javascript:alert(1)")]
    [InlineData("not a url at all")]
    public void ValidateJobUrl_RejectsAnythingNotAWebLink(string url) =>
        Assert.NotNull(ValidateUrl(url));

    [Fact]
    public void ValidateJobUrl_RejectsAnOverlongLink()
    {
        // The column is capped at 2000; catching it here beats a DbUpdateException on save.
        var tooLong = "https://acme.example/" + new string('a', 2100);

        Assert.NotNull(ValidateUrl(tooLong));
    }

    private static ValidationResult? ValidateUrl(string? url) =>
        ApplicationEditorViewModel.ValidateJobUrl(
            url,
            new ValidationContext(new object()));

    [Fact]
    public void ValidateDateApplied_RejectsTomorrowOnTheViewModelsOwnClock()
    {
        var vm = NewEditorViewModel();
        var context = new ValidationContext(vm);

        Assert.Null(ApplicationEditorViewModel.ValidateDateApplied(Clock.Today, context));
        Assert.Null(ApplicationEditorViewModel.ValidateDateApplied(Clock.Today.AddDays(-30), context));
        Assert.NotNull(ApplicationEditorViewModel.ValidateDateApplied(Clock.Today.AddDays(1), context));
    }

    [Fact]
    public void ValidateDateApplied_FollowsTheClockRatherThanTheMachine()
    {
        var vm = NewEditorViewModel();
        var context = new ValidationContext(vm);
        var date = Clock.Today.AddDays(5);

        // Future today, so rejected...
        Assert.NotNull(ApplicationEditorViewModel.ValidateDateApplied(date, context));

        // ...and the same date is fine once the clock has caught up to it. The rule is a function
        // of the injected clock, not of the wall clock the test happens to run at.
        Clock.AdvanceDays(5);
        Assert.Null(ApplicationEditorViewModel.ValidateDateApplied(date, context));
    }

    // ---------------- Loading ----------------

    [Fact]
    public async Task Load_FillsEveryFieldAndTheTimelineNewestFirst()
    {
        var seeded = await SeedAsync(b => b
            .At("Timeline Co").For("Engineer").WithStatus(ApplicationStatus.Applied));

        Clock.AdvanceDays(4);
        await using (var scope = Repositories.Create())
        {
            await scope.Repository.ChangeStatusAsync(seeded.Id, ApplicationStatus.Interview, "Recruiter called");
        }

        var vm = NewEditorViewModel();
        await vm.LoadAsync(seeded.Id);

        Assert.Equal("Edit application", vm.Title);
        Assert.Equal("Save changes", vm.SaveLabel);
        Assert.Equal("Timeline Co", vm.CompanyName);
        Assert.Equal(ApplicationStatus.Interview, vm.Status);

        // Newest first: the timeline reads top-down as "most recent thing that happened".
        Assert.True(vm.HasStatusHistory);
        Assert.Equal(
            new[] { ApplicationStatus.Interview, ApplicationStatus.Applied },
            vm.StatusHistory.Select(h => h.Status));
        Assert.Equal("Recruiter called", vm.StatusHistory[0].Note);
    }

    [Fact]
    public async Task Load_OnADeletedApplication_ReportsItAndGoesBack()
    {
        var vm = NewEditorViewModel();

        await vm.LoadAsync(98765);

        Assert.True(Dialogs.ErrorWasShown);
        Assert.Equal(1, Navigation.ApplicationsCount);
    }

    // ---------------- Saving ----------------

    [Fact]
    public async Task Save_WithMissingRequiredFields_WritesNothing()
    {
        var vm = await NewLoadedEditorAsync(b => b.At("Intact Co").For("Engineer"));

        vm.CompanyName = "";
        vm.JobTitle = "";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.True(vm.HasErrors);

        // The stored row keeps the values it had rather than being blanked out.
        var stored = Assert.Single(await AllAsync());
        Assert.Equal("Intact Co", stored.CompanyName);
        Assert.Equal("Engineer", stored.JobTitle);

        // It must also stay on the form rather than navigating away from unsaved work.
        Assert.Equal(0, Navigation.ApplicationsCount);
    }

    [Fact]
    public async Task Save_TrimsWhitespaceAndStoresBlankOptionalFieldsAsNull()
    {
        var vm = await NewLoadedEditorAsync(b => b.At("Before").WithNotes("Old notes"));

        vm.CompanyName = "  Spaced Co  ";
        vm.JobTitle = "  Engineer  ";
        vm.Location = "  Remote  ";
        vm.Notes = "   ";

        await vm.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(await AllAsync());
        Assert.Equal("Spaced Co", saved.CompanyName);
        Assert.Equal("Engineer", saved.JobTitle);
        Assert.Equal("Remote", saved.Location);

        // Null rather than "   ", so the detail pane's "is there anything here?" checks work.
        Assert.Null(saved.Notes);
    }

    [Fact]
    public async Task Save_ChangingTheStage_AppendsExactlyOneHistoryEntryWithTheNote()
    {
        var seeded = await SeedAsync(b => b.At("Moving Co").WithStatus(ApplicationStatus.Applied));

        var vm = NewEditorViewModel();
        await vm.LoadAsync(seeded.Id);

        Assert.False(vm.IsStatusChanging);
        vm.Status = ApplicationStatus.Interview;
        Assert.True(vm.IsStatusChanging);

        vm.InterviewRound = 1;
        vm.StatusChangeNote = "  First round booked  ";
        Clock.AdvanceDays(6);

        await vm.SaveCommand.ExecuteAsync(null);

        var reloaded = (await LoadAsync(seeded.Id))!;

        Assert.Equal(ApplicationStatus.Interview, reloaded.Status);
        Assert.Equal(1, reloaded.InterviewRound);
        Assert.Equal(
            new[] { ApplicationStatus.Applied, ApplicationStatus.Interview },
            reloaded.StatusHistory.Select(h => h.Status));
        Assert.Equal("First round booked", reloaded.StatusHistory.Last().Note);
    }

    [Fact]
    public async Task Save_WithoutTouchingTheStage_AddsNoHistoryEntry()
    {
        var seeded = await SeedAsync(b => b.At("Steady Co").WithStatus(ApplicationStatus.Applied));

        var vm = NewEditorViewModel();
        await vm.LoadAsync(seeded.Id);
        vm.Location = "Somewhere new";

        await vm.SaveCommand.ExecuteAsync(null);

        var reloaded = (await LoadAsync(seeded.Id))!;

        // Editing a field is not a pipeline move. A spurious entry here would inflate the
        // interview and rejection rates, which are read off the history.
        Assert.Equal("Somewhere new", reloaded.Location);
        Assert.Single(reloaded.StatusHistory);
    }

    [Fact]
    public async Task Save_MovingAwayFromInterview_ClearsTheRoundNumberAndTheInterviewDate()
    {
        var seeded = await SeedAsync(b => b
            .At("Round Co")
            .WithStatus(
                ApplicationStatus.Interview,
                interviewRound: 2,
                interviewDate: new DateOnly(2026, 9, 20)));

        var vm = NewEditorViewModel();
        await vm.LoadAsync(seeded.Id);
        Assert.True(vm.ShowsInterviewFields);
        Assert.Equal(2, vm.InterviewRound);
        Assert.Equal(new DateOnly(2026, 9, 20), vm.InterviewDate);

        vm.Status = ApplicationStatus.Rejected;

        // Both interview fields disappear from the form the moment the stage leaves Interview.
        Assert.False(vm.ShowsInterviewFields);
        Assert.Null(vm.InterviewRound);
        Assert.Null(vm.InterviewDate);

        await vm.SaveCommand.ExecuteAsync(null);

        var reloaded = (await LoadAsync(seeded.Id))!;
        Assert.Null(reloaded.InterviewRound);
        Assert.Null(reloaded.InterviewDate);
    }

    [Fact]
    public async Task Save_MovingIntoInterview_KeepsTheRoundNumberAndTheInterviewDate()
    {
        var seeded = await SeedAsync(b => b.At("Into Co").WithStatus(ApplicationStatus.Applied));

        var vm = NewEditorViewModel();
        await vm.LoadAsync(seeded.Id);

        vm.Status = ApplicationStatus.Interview;
        vm.InterviewRound = 1;
        vm.InterviewDate = new DateOnly(2026, 9, 22);

        await vm.SaveCommand.ExecuteAsync(null);

        // ChangeStatusAsync clears both on the way out of Interview, so a move *into* it has to
        // put them back after the transition or the save silently loses what the user typed.
        var reloaded = (await LoadAsync(seeded.Id))!;
        Assert.Equal(ApplicationStatus.Interview, reloaded.Status);
        Assert.Equal(1, reloaded.InterviewRound);
        Assert.Equal(new DateOnly(2026, 9, 22), reloaded.InterviewDate);
    }

    // ---------------- Child rows ----------------

    [Fact]
    public async Task Save_DropsBlankChildRowsInsteadOfRefusingTheWholeSave()
    {
        var vm = await NewLoadedEditorAsync();

        vm.AddContactCommand.Execute(null);   // left blank

        vm.AddContactCommand.Execute(null);
        vm.Contacts.Last().Name = "Dana Reed";
        vm.Contacts.Last().Email = "dana.reed@acme.example";

        await vm.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(await AllAsync());
        var reloaded = (await LoadAsync(saved.Id))!;

        // Clicking "Add contact" and changing your mind should not block the save, and must not
        // trip the "a contact needs an email" check either.
        Assert.Equal("Dana Reed", Assert.Single(reloaded.Contacts).Name);
    }

    [Fact]
    public async Task Save_RefusesAContactWithNoEmail()
    {
        var vm = await NewLoadedEditorAsync();

        vm.AddContactCommand.Execute(null);
        vm.Contacts.Last().Name = "Dana Reed";

        await vm.SaveCommand.ExecuteAsync(null);

        // A half-filled row is a mistake worth pointing at, unlike an entirely blank one.
        Assert.True(Dialogs.ErrorWasShown);
        Assert.Contains("Dana Reed", Dialogs.ErrorMessages[0]);

        // And nothing is written: the whole save is refused, not just the offending row.
        var saved = Assert.Single(await AllAsync());
        Assert.Empty((await LoadAsync(saved.Id))!.Contacts);
        Assert.Equal(0, Navigation.ApplicationsCount);
    }

    [Fact]
    public async Task Save_EditingAChildRow_KeepsItsIdentity()
    {
        var seeded = await SeedAsync(b => b
            .At("Children Co")
            .WithContact("Keep Me", "keep@example.com", "Recruiter")
            .WithContact("Remove Me", "remove@example.com"));

        var originalKeptId = (await LoadAsync(seeded.Id))!
            .Contacts.Single(c => c.Name == "Keep Me").Id;

        var vm = NewEditorViewModel();
        await vm.LoadAsync(seeded.Id);

        vm.Contacts.Single(c => c.Name == "Keep Me").Role = "Hiring Manager";
        vm.RemoveContactCommand.Execute(vm.Contacts.Single(c => c.Name == "Remove Me"));
        vm.AddContactCommand.Execute(null);
        vm.Contacts.Last().Name = "New Person";
        vm.Contacts.Last().Email = "new@example.com";

        await vm.SaveCommand.ExecuteAsync(null);

        var reloaded = (await LoadAsync(seeded.Id))!;

        Assert.Equal(2, reloaded.Contacts.Count);

        // SyncChildren matches on id, so an edited row is updated in place rather than deleted
        // and reinserted - which would churn ids and lose any future foreign key to them.
        var kept = reloaded.Contacts.Single(c => c.Name == "Keep Me");
        Assert.Equal(originalKeptId, kept.Id);
        Assert.Equal("Hiring Manager", kept.Role);
        Assert.Contains(reloaded.Contacts, c => c.Name == "New Person");
        Assert.DoesNotContain(reloaded.Contacts, c => c.Name == "Remove Me");
    }

    [Fact]
    public async Task RemoveCommands_IgnoreANullRow()
    {
        var vm = await NewLoadedEditorAsync();
        vm.AddContactCommand.Execute(null);

        // The command is bound with a CommandParameter that can be null while a row is being
        // recycled by the virtualising panel.
        vm.RemoveContactCommand.Execute(null);

        Assert.Single(vm.Contacts);
    }

    [Fact]
    public async Task Cancel_GoesBackWithoutSaving()
    {
        var vm = await NewLoadedEditorAsync(b => b.At("Unchanged Co"));

        vm.CompanyName = "Typed but abandoned";
        vm.CancelCommand.Execute(null);

        Assert.Equal(1, Navigation.ApplicationsCount);
        Assert.Equal("Unchanged Co", Assert.Single(await AllAsync()).CompanyName);
    }
}
