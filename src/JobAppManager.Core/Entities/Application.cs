using JobAppManager.Core.Enums;

namespace JobAppManager.Core.Entities;

/// <summary>One job applied to. The root record every screen is a view over.</summary>
public class Application
{
    public int Id { get; set; }

    public DateOnly DateApplied { get; set; }

    public string CompanyName { get; set; } = string.Empty;

    public string JobTitle { get; set; } = string.Empty;

    public string Location { get; set; } = string.Empty;

    public InterestLevel InterestLevel { get; set; } = InterestLevel.Yellow;

    public ApplicationStatus Status { get; set; } = ApplicationStatus.NoResponse;

    /// <summary>Which interview round, when <see cref="Status"/> is
    /// <see cref="ApplicationStatus.Interview"/>. Null otherwise.</summary>
    public int? InterviewRound { get; set; }

    public bool FromJobFair { get; set; }

    public string? Notes { get; set; }

    /// <summary>Stamped by <c>JobAppContext.SaveChanges</c>; never set this by hand.</summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>Stamped by <c>JobAppContext.SaveChanges</c>; never set this by hand.</summary>
    public DateTime UpdatedUtc { get; set; }

    public ICollection<SubmittedItem> SubmittedItems { get; set; } = new List<SubmittedItem>();

    public ICollection<Contact> Contacts { get; set; } = new List<Contact>();
}
