using CRM.Domain.Common;
using CRM.Domain.Enums;

namespace CRM.Domain.Entities;

public class Lead : CallCenterEntity
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public DateTime? DateOfBirth { get; set; }

    // ---- Jornaya intake (captured by the Fronter) ----
    public string? MaritalStatus { get; set; }
    /// <summary>Age in years captured at intake (mirrors the lead form's "Age Years").</summary>
    public int? AgeYears { get; set; }

    public WorkflowStage Stage { get; set; } = WorkflowStage.New;
    public LeadDisposition Disposition { get; set; } = LeadDisposition.None;
    /// <summary>Status set by the Verifier while the lead is in the verification queue.</summary>
    public VerifierStatus VerifierStatus { get; set; } = VerifierStatus.None;
    public string? Source { get; set; }
    public string? JornayaLeadId { get; set; }
    public bool JornayaVerified { get; set; }
    public DateTime? JornayaVerifiedAt { get; set; }

    public Guid? AssignedUserId { get; set; }
    public Guid? TeamId { get; set; }
    public Guid? VerticalId { get; set; }
    public Guid? CampaignId { get; set; }
    public Guid? LeadSourceId { get; set; }
    public string? RequiredSkillCode { get; set; }
    public bool ConsentCaptured { get; set; }
    public int Score { get; set; }

    public string? Notes { get; set; }

    public ICollection<LeadActivity> Activities { get; set; } = new List<LeadActivity>();
    public ICollection<ScheduledCallback> Callbacks { get; set; } = new List<ScheduledCallback>();
    public Sale? Sale { get; set; }
    public LeadApplication? Application { get; set; }
}

public class LeadActivity : CallCenterEntity
{
    public Guid LeadId { get; set; }
    public Guid UserId { get; set; }
    public WorkflowStage FromStage { get; set; }
    public WorkflowStage ToStage { get; set; }
    public LeadDisposition Disposition { get; set; }
    public string? Notes { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

public class ScheduledCallback : CallCenterEntity
{
    public Guid LeadId { get; set; }
    public Guid AssignedUserId { get; set; }
    public DateTime ScheduledFor { get; set; }
    public string? Reason { get; set; }
    public bool Completed { get; set; }
    public bool Reminded { get; set; }
}

public class Sale : CallCenterEntity
{
    public Guid LeadId { get; set; }
    public Guid CloserUserId { get; set; }
    public Guid? ValidatorUserId { get; set; }
    public string Carrier { get; set; } = string.Empty;
    public string? PolicyNumber { get; set; }
    public decimal MonthlyPremium { get; set; }
    public decimal AnnualPremium { get; set; }
    public DateTime SoldAt { get; set; } = DateTime.UtcNow;
    public DateTime? ValidatedAt { get; set; }
    public DateTime? FundedAt { get; set; }
    public bool IsInternalSale { get; set; }
    public string? InternalSaleReason { get; set; }

    /// <summary>
    /// Banking result code captured at submission. Submission rules:
    ///   103 = clear → may submit;
    ///   198 = submit allowed but a verification recording must be attached;
    ///   anything else → submission blocked.
    /// </summary>
    public int BankingCode { get; set; }

    /// <summary>Opaque storage key for the verification recording (optional for BankingCode == 198).</summary>
    public string? RecordingUrl { get; set; }

    /// <summary>Closer's reason for proceeding when Lyons flags the account (banking code 198).</summary>
    public string? BankingNote { get; set; }

    // ---- Lyons bank-account validation (banking code is derived from this, never entered by hand) ----
    /// <summary>ABA routing number of the validated bank account (not sensitive).</summary>
    public string? BankRoutingNumber { get; set; }
    /// <summary>Last four digits of the validated bank account (full number is never stored).</summary>
    public string? BankAccountLast4 { get; set; }
    /// <summary>Bank name returned by Lyons.</summary>
    public string? BankName { get; set; }
    /// <summary>Lyons reference id for the validation, kept for audit/dispute.</summary>
    public string? LyonsReference { get; set; }

    // ---- Validator review (post-close validation queue) ----
    /// <summary>Where the sale sits in the Validator queue. Defaults to Completed on submission.</summary>
    public ValidatorStatus ValidatorStatus { get; set; } = ValidatorStatus.Completed;
    /// <summary>Carrier the customer was approved on (set when the validator marks Approved).</summary>
    public string? CarrierApproved { get; set; }
    /// <summary>Approved coverage / face amount (set when Approved).</summary>
    public decimal? CoverageApproved { get; set; }
    /// <summary>Approved premium (set when Approved).</summary>
    public decimal? PremiumApproved { get; set; }
    /// <summary>Approved plan (set when Approved).</summary>
    public string? PlanApproved { get; set; }
    /// <summary>Reason recorded when the validator marks the sale Declined.</summary>
    public string? DeclineReason { get; set; }
}
