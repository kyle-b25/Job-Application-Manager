namespace JobAppManager.Core.Enums;

/// <summary>Where an application currently stands. Three stages, in order: an application is
/// <see cref="Applied"/> when it goes out, <see cref="Interview"/> once anyone agrees to talk,
/// and <see cref="Rejected"/> if it dies - which can happen from either of the other two.
/// Every move is recorded as an <see cref="Entities.StatusChange"/>; rounds within
/// <see cref="Interview"/> are counted on <see cref="Entities.Application.InterviewRound"/> so
/// "interview #X" needs one status, not many.</summary>
public enum ApplicationStatus
{
    Applied = 0,
    Interview = 1,
    Rejected = 2
}
