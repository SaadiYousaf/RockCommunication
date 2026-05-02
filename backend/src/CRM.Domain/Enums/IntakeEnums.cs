namespace CRM.Domain.Enums;

/// <summary>
/// Outcome a Verifier records for a fronted lead. "Verified" advances the lead
/// to the Closer queue; every other value keeps or retires it from verification.
/// </summary>
public enum VerifierStatus
{
    None = 0,
    Verified = 1,
    NotInterested = 2,
    Dnc = 3,
    Busy = 4,
    CallBack = 5,
    DeadAir = 6
}

/// <summary>
/// Outcome a Closer records for a verified lead. "CompleteAndSold" creates the
/// sale; the remaining values retire the lead with a reason.
/// </summary>
public enum CloserStatus
{
    None = 0,
    CompleteAndSold = 1,
    LostOnSocial = 2,
    LostOnAccount = 3,
    DncLead = 4,
    NotInterestedCallback = 5
}
