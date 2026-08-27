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

    public string? JobUrl { get; set; }

    public InterestLevel InterestLevel { get; set; } = InterestLevel.Yellow;

    /// <summary>The latest status. Move it with <c>IApplicationRepository.ChangeStatusAsync</c>
    /// rather than assigning here, so <see cref="StatusHistory"/> stays in step.</summary>
    public ApplicationStatus Status { get; set; } = ApplicationStatus.Applied;

    /// <summary>Which interview round, when <see cref="Status"/> is
    /// <see cref="ApplicationStatus.Interview"/>. Null otherwise.</summary>
    public int? InterviewRound { get; set; }

    /// <summary>When the interview is, or was. Only meaningful while <see cref="Status"/> is
    /// <see cref="ApplicationStatus.Interview"/>; cleared on the way out, like
    /// <see cref="InterviewRound"/>.</summary>
    public DateOnly? InterviewDate { get; set; }

    public bool FromJobFair { get; set; }

    /// <summary>Whether a resume went with this application. A flag rather than a named row:
    /// the useful question is "did I send one", not "which version".</summary>
    public bool ResumeSubmitted { get; set; }

    /// <summary>Whether a cover letter went with this application.</summary>
    public bool CoverLetterSubmitted { get; set; }

    public string? Notes { get; set; }

    /// <summary>Stamped by <c>JobAppContext.SaveChanges</c>; never set this by hand.</summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>Stamped by <c>JobAppContext.SaveChanges</c>; never set this by hand.</summary>
    public DateTime UpdatedUtc { get; set; }

    public ICollection<Contact> Contacts { get; set; } = new List<Contact>();

    /// <summary>Every status this application has been in, oldest first.</summary>
    public ICollection<StatusChange> StatusHistory { get; set; } = new List<StatusChange>();
}
