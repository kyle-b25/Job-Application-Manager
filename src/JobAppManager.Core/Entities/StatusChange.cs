using JobAppManager.Core.Enums;

namespace JobAppManager.Core.Entities;

/// <summary>One move of an application into a status, kept forever. The current
/// <see cref="Entities.Application.Status"/> is the latest of these; the rest is what lets the
/// dashboard answer "how long did this sit in Applied before an interview?".</summary>
public class StatusChange
{
    public int Id { get; set; }

    public int ApplicationId { get; set; }

    /// <summary>The status moved *into*.</summary>
    public ApplicationStatus Status { get; set; }

    /// <summary>Stamped by <c>JobAppContext.SaveChanges</c>; never set this by hand.</summary>
    public DateTime ChangedUtc { get; set; }

    public string? Note { get; set; }

    public Application? Application { get; set; }
}
