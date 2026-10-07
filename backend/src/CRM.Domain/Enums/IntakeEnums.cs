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
    ClientCancelled = 7,
    /// <summary>
    /// The application has an error the closer must fix. The specific error (e.g.
    /// "Wrong banking / Payor issue", "Identity Error") is captured in the reason field.
    /// </summary>
    ErrorInApplicationInformation = 8,
    /// <summary>
    /// The carrier clawed the advanced commission back (policy lapsed/cancelled after payout).
    /// Set by the Commission Agent: the sale's commission entries are negated and become editable
    /// so the desk can reconcile the true amounts. See CommissionDesk.
    /// </summary>
    ChargedBack = 9,
    /// <summary>
    /// Handed to Head Office — the submission agent cannot resolve it from the floor and HO is now
    /// carrying it. Still open work, not an outcome: the sale sits here until HO reports back and it
    /// moves on to Approved, Decline or whatever HO finds.
    /// </summary>
    ReferredToHo = 10,
    /// <summary>
    /// The customer themselves is the problem — abusive, fraudulent, or not to be sold to again.
    /// Requires a note saying why.
    ///
    /// Unlike every other status, this one REMOVES the sale from the submission queue: there is no
    /// submission work left to do on it and leaving it there means a submission agent re-reads the
    /// same dead row every day. It stays in the sales list, because the sale happened and the
    /// figures have to reconcile.
    /// </summary>
    BadCustomer = 11,
    /// <summary>Customer does not want it, or asked not to be contacted again.</summary>
    NotInterestedOrDnc = 12,
    /// <summary>The call ended at the signature step — the application was never completed.</summary>
    CallDroppedOnSignature = 13,
    /// <summary>No carrier available to write this policy in the customer's state.</summary>
    StateNotAvailable = 14
}

/// <summary>Sub-reason for <see cref="ValidatorStatus.ErrorInApplicationInformation"/>.</summary>
public enum ApplicationErrorReason
{
    None = 0,
    WrongBankingOrPayorIssue = 1,
    IdentityError = 2
}
