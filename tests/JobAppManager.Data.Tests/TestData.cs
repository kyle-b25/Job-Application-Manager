using JobAppManager.Core.Entities;
using JobAppManager.Core.Enums;

namespace JobAppManager.Data.Tests;

internal static class TestData
{
    /// <summary>A fully populated application, so round-trip tests cover every column.</summary>
    public static Application FullApplication(
        string company = "Acme Corp",
        string title = "Software Engineer",
        DateOnly? dateApplied = null) => new()
    {
        DateApplied = dateApplied ?? new DateOnly(2026, 8, 20),
        CompanyName = company,
        JobTitle = title,
        Location = "Remote - US",
        InterestLevel = InterestLevel.Green,
        Status = ApplicationStatus.Interview,
        InterviewRound = 2,
        FromJobFair = true,
        Notes = "Met the hiring manager at the fall career fair.",
        SubmittedItems =
        {
            new SubmittedItem
            {
                Kind = SubmissionKind.Resume,
                Name = "Resume - Backend v3",
                SubmittedOn = new DateOnly(2026, 8, 20)
            },
            new SubmittedItem
            {
                Kind = SubmissionKind.Assessment,
                Name = "HackerRank screen",
                SubmittedOn = new DateOnly(2026, 8, 22)
            }
        },
        Contacts =
        {
            new Contact
            {
                Name = "Dana Reed",
                Email = "dana.reed@acme.example",
                Role = "Recruiter",
                DateContacted = new DateOnly(2026, 8, 21)
            }
        }
    };

    /// <summary>A minimal application, for tests that only care about one or two fields.</summary>
    public static Application Minimal(
        string company,
        DateOnly dateApplied,
        ApplicationStatus status = ApplicationStatus.NoResponse,
        InterestLevel interest = InterestLevel.Yellow,
        string title = "Engineer",
        string location = "Boston, MA",
        bool fromJobFair = false) => new()
    {
        DateApplied = dateApplied,
        CompanyName = company,
        JobTitle = title,
        Location = location,
        Status = status,
        InterestLevel = interest,
        FromJobFair = fromJobFair
    };
}
