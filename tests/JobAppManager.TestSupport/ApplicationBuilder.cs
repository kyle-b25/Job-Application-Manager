using JobAppManager.Core.Entities;
using JobAppManager.Core.Enums;

namespace JobAppManager.TestSupport;

/// <summary>Builds an <see cref="Application"/> for a test, naming only the fields that test
/// actually cares about.
///
/// Replaces stacking more optional parameters onto <see cref="TestData"/>: those two factories
/// already carry seven between them, and at that width a call site reads as a row of positional
/// values whose meaning has to be looked up.</summary>
public sealed class ApplicationBuilder
{
    private readonly Application _application = new()
    {
        CompanyName = "Acme Corp",
        JobTitle = "Software Engineer",
        Location = "Boston, MA",
        DateApplied = new DateOnly(2026, 8, 20),
        InterestLevel = InterestLevel.Yellow,
        Status = ApplicationStatus.Applied
    };

    public static ApplicationBuilder An() => new();

    public ApplicationBuilder At(string company)
    {
        _application.CompanyName = company;
        return this;
    }

    public ApplicationBuilder For(string jobTitle)
    {
        _application.JobTitle = jobTitle;
        return this;
    }

    public ApplicationBuilder In(string location)
    {
        _application.Location = location;
        return this;
    }

    public ApplicationBuilder AppliedOn(DateOnly date)
    {
        _application.DateApplied = date;
        return this;
    }

    public ApplicationBuilder AppliedOn(int year, int month, int day) =>
        AppliedOn(new DateOnly(year, month, day));

    public ApplicationBuilder WithStatus(ApplicationStatus status, int? interviewRound = null)
    {
        _application.Status = status;
        _application.InterviewRound = interviewRound;
        return this;
    }

    public ApplicationBuilder WithInterest(InterestLevel level)
    {
        _application.InterestLevel = level;
        return this;
    }

    public ApplicationBuilder FromJobFair(bool fromJobFair = true)
    {
        _application.FromJobFair = fromJobFair;
        return this;
    }

    public ApplicationBuilder WithUrl(string? url)
    {
        _application.JobUrl = url;
        return this;
    }

    public ApplicationBuilder WithSalary(string? salaryRange)
    {
        _application.SalaryRange = salaryRange;
        return this;
    }

    public ApplicationBuilder WithNotes(string? notes)
    {
        _application.Notes = notes;
        return this;
    }

    public ApplicationBuilder WithSubmittedItem(
        string name,
        SubmissionKind kind = SubmissionKind.Resume,
        DateOnly? submittedOn = null)
    {
        _application.SubmittedItems.Add(new SubmittedItem
        {
            Kind = kind,
            Name = name,
            SubmittedOn = submittedOn
        });

        return this;
    }

    public ApplicationBuilder WithContact(
        string name,
        string email,
        string? role = null,
        DateOnly? dateContacted = null)
    {
        _application.Contacts.Add(new Contact
        {
            Name = name,
            Email = email,
            Role = role,
            DateContacted = dateContacted
        });

        return this;
    }

    public Application Build() => _application;

    public static implicit operator Application(ApplicationBuilder builder) => builder.Build();
}
