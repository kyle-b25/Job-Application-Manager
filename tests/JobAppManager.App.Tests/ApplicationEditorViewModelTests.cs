using System.ComponentModel.DataAnnotations;
using JobAppManager.App.ViewModels;
using JobAppManager.Core.Enums;
using Xunit;

namespace JobAppManager.App.Tests;

/// <summary>The add/edit form: validation, the new-vs-edit split, child rows, and the rule that
/// pipeline moves go through the repository so history is recorded.</summary>
public class ApplicationEditorViewModelTests : ViewModelTestBase
{
    private ApplicationEditorViewModel NewFilledEditor()
    {
        var vm = NewEditorViewModel();
        vm.LoadNew();
        vm.CompanyName = "Northwind Labs";
        vm.JobTitle = "Senior Backend Engineer";
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

    [Fact]
    public void NewApplication_DefaultsToTodayOnTheInjectedClock()
    {
        var vm = NewEditorViewModel();

        Assert.Equal(Clock.Today, vm.DateApplied);

        Clock.AdvanceDays(3);
        vm.LoadNew();
        Assert.Equal(Clock.Today, vm.DateApplied);
    }

    // ---------------- New vs edit ----------------

    [Fact]
    public void LoadNew_PresentsABlankFormInNewMode()
    {
        var vm = NewFilledEditor();

        Assert.True(vm.IsNew);
        Assert.Equal("Add application", vm.Title);
        Assert.Equal("Add application", vm.SaveLabel);
        Assert.False(vm.HasStatusHistory);
    }

    [Fact]
    public async Task LoadNew_ClearsEverythingLeftOverFromAPreviousEdit()
    {
        var seeded = await SeedAsync(b => b
            .At("Leftover Co").For("Engineer")
            .WithUrl("https://leftover.example/1")
            .WithNotes("Some notes")
            .WithStatus(ApplicationStatus.Interview, interviewRound: 3)
            .WithContact("Someone", "someone@example.com")
            .WithResume().WithCoverLetter());

        var vm = NewEditorViewModel();
        await vm.LoadAsync(seeded.Id);
        Assert.False(vm.IsNew);

        vm.LoadNew();

        // The editor is a singleton reused across navigations, so anything not reset here leaks
        // into the next "Add New" - the user would see the previous company's contacts.
        Assert.True(vm.IsNew);
        Assert.Equal(string.Empty, vm.CompanyName);
        Assert.Equal(string.Empty, vm.JobTitle);
        Assert.Null(vm.JobUrl);
        Assert.Null(vm.Notes);
        Assert.Null(vm.InterviewRound);
        Assert.Equal(ApplicationStatus.Applied, vm.Status);
        Assert.False(vm.ResumeSubmitted);
        Assert.False(vm.CoverLetterSubmitted);
        Assert.Empty(vm.Contacts);
        Assert.Empty(vm.StatusHistory);
    }

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

        Assert.False(vm.IsNew);
        Assert.Equal("Edit application", vm.Title);
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
        var vm = NewEditorViewModel();
        vm.LoadNew();
        vm.CompanyName = "";
        vm.JobTitle = "";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.True(vm.HasErrors);
        Assert.Empty(await AllAsync());

        // It must also stay on the form rather than navigating away from unsaved work.
        Assert.Equal(0, Navigation.ApplicationsCount);
    }

    [Fact]
    public async Task Save_TrimsWhitespaceAndStoresBlankOptionalFieldsAsNull()
    {
        var vm = NewEditorViewModel();
        vm.LoadNew();
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
    public async Task Save_OnANewApplication_SeedsExactlyOneHistoryEntry()
    {
        var vm = NewFilledEditor();
        vm.Status = ApplicationStatus.Interview;

        await vm.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(await AllAsync());
        var reloaded = (await LoadAsync(saved.Id))!;

        var entry = Assert.Single(reloaded.StatusHistory);
        Assert.Equal(ApplicationStatus.Interview, entry.Status);
        Assert.Equal(1, Navigation.ApplicationsCount);
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

        // Editing a field is not a pipeline move. A spurious entry here would corrupt every
        // stage-duration average on the dashboard.
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
        var vm = NewFilledEditor();

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
        var vm = NewFilledEditor();

        vm.AddContactCommand.Execute(null);
        vm.Contacts.Last().Name = "Dana Reed";

        await vm.SaveCommand.ExecuteAsync(null);

        // A half-filled row is a mistake worth pointing at, unlike an entirely blank one.
        Assert.True(Dialogs.ErrorWasShown);
        Assert.Contains("Dana Reed", Dialogs.ErrorMessages[0]);
        Assert.Empty(await AllAsync());
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
    public void RemoveCommands_IgnoreANullRow()
    {
        var vm = NewFilledEditor();
        vm.AddContactCommand.Execute(null);

        // The command is bound with a CommandParameter that can be null while a row is being
        // recycled by the virtualising panel.
        vm.RemoveContactCommand.Execute(null);

        Assert.Single(vm.Contacts);
    }

    [Fact]
    public void Cancel_GoesBackWithoutSaving()
    {
        var vm = NewFilledEditor();

        vm.CancelCommand.Execute(null);

        Assert.Equal(1, Navigation.ApplicationsCount);
    }
}
