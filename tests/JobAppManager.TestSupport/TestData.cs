using JobAppManager.Core.Entities;
using JobAppManager.Core.Enums;

namespace JobAppManager.TestSupport;

/// <summary>The two shapes most tests want. Kept as named factories over
/// <see cref="ApplicationBuilder"/> so the existing tests read the same as before; anything
/// needing a different shape should use the builder directly rather than growing a parameter here.
/// </summary>
public static class TestData
{
    /// <summary>A fully populated application, so round-trip tests cover every column.</summary>
    public static Application FullApplication(
        string company = "Acme Corp",
        string title = "Software Engineer",
        DateOnly? dateApplied = null) =>
        ApplicationBuilder.An()
            .At(company)
            .For(title)
            .In("Remote - US")
            .AppliedOn(dateApplied ?? new DateOnly(2026, 8, 20))
            .WithUrl("https://acme.example/careers/12345")
            .WithInterest(InterestLevel.Green)
            .WithStatus(ApplicationStatus.Interview, interviewRound: 2, interviewDate: new DateOnly(2026, 8, 28))
            .FromJobFair()
            .WithNotes("Met the hiring manager at the fall career fair.")
            .WithResume()
            .WithCoverLetter()
            .WithContact("Dana Reed", "dana.reed@acme.example", "Recruiter", new DateOnly(2026, 8, 21))
            .Build();

    /// <summary>A minimal application, for tests that only care about one or two fields.</summary>
    public static Application Minimal(
        string company,
        DateOnly dateApplied,
        ApplicationStatus status = ApplicationStatus.Applied,
        InterestLevel interest = InterestLevel.Yellow,
        string title = "Engineer",
        string location = "Boston, MA",
        bool fromJobFair = false) =>
        ApplicationBuilder.An()
            .At(company)
            .For(title)
            .In(location)
            .AppliedOn(dateApplied)
            .WithStatus(status)
            .WithInterest(interest)
            .FromJobFair(fromJobFair)
            .Build();
}
