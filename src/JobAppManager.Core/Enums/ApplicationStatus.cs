namespace JobAppManager.Core.Enums;

/// <summary>Where an application currently stands. Interview rounds are tracked separately
/// on <see cref="Entities.Application.InterviewRound"/> so "interview #X" needs one status, not many.</summary>
public enum ApplicationStatus
{
    NoResponse = 0,
    Interview = 1,
    Rejected = 2,
    Accepted = 3
}
