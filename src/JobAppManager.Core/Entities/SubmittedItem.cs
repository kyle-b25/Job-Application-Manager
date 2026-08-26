using JobAppManager.Core.Enums;

namespace JobAppManager.Core.Entities;

/// <summary>Something handed to the recruiter, named: a specific resume version, a cover
/// letter, a take-home assessment. A child table because the set is open-ended.</summary>
public class SubmittedItem
{
    public int Id { get; set; }

    public int ApplicationId { get; set; }

    public SubmissionKind Kind { get; set; }

    /// <summary>The specific document or test name, e.g. "Resume - Backend v3".</summary>
    public string Name { get; set; } = string.Empty;

    public DateOnly? SubmittedOn { get; set; }

    public Application? Application { get; set; }
}
