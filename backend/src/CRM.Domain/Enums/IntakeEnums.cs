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

/// <summary>
/// Status the Validator sets on a submitted sale in the Validator queue. A sale
/// lands here as <see cref="Completed"/> the moment the closer submits it, and the
/// validator works it through approval, funding, or one of the rejection states.
/// </summary>
public enum ValidatorStatus
{
    /// <summary>Default — the sale was just submitted by the closer and is awaiting validation.</summary>
    Completed = 0,
    /// <summary>Customer approved on a carrier; the validator fills the approved carrier/coverage/premium/plan.</summary>
    Approved = 1,
    /// <summary>Customer has paid the premium and the carrier has paid commission.</summary>
    ActivePaid = 2,
    /// <summary>No update in commission from the carrier yet.</summary>
    NoUpdateInCommission = 3,
    /// <summary>Bank details were rejected (invalid account).</summary>
    BadBank = 4,
    /// <summary>Non-sufficient funds on draft.</summary>
    Nsf = 5,
    /// <summary>Application declined — a reason is required.</summary>
    Decline = 6,
    /// <summary>Client cancelled the policy.</summary>
    ClientCancelled = 7
}
