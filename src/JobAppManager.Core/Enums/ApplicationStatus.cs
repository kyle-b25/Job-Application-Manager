namespace JobAppManager.Core.Enums;

/// <summary>Where an application currently stands. The stages form a pipeline, but movement
/// through it is not strictly forward - anything can go to Rejected or Withdrawn at any point.
/// Every move is recorded as an <see cref="Entities.StatusChange"/>; interview rounds within
/// <see cref="Interview"/> are counted on <see cref="Entities.Application.InterviewRound"/> so
/// "interview #X" needs one status, not many.</summary>
public enum ApplicationStatus
{
    Wishlist = 0,
    Applied = 1,
    PhoneScreen = 2,
    Interview = 3,
    Offer = 4,
    Rejected = 5,
    Withdrawn = 6
}
