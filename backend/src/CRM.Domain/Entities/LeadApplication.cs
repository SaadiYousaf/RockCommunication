using CRM.Domain.Common;
using CRM.Domain.Enums;

namespace CRM.Domain.Entities;

/// <summary>
/// The closing application a Closer completes for a verified lead (health,
/// policy, and banking details) plus the closing outcome. One per lead.
///
/// NOTE: this record holds sensitive PII (SSN, bank account / routing, driver's
/// licence). It should be encrypted at rest in production; only the last four
/// digits of the bank account flow onto the <see cref="Sale"/>.
/// </summary>
public class LeadApplication : CallCenterEntity
{
    public Guid LeadId { get; set; }

    // ---- Health / applicant ----
    public string? HealthConditions { get; set; }
    public string? Gender { get; set; }
    public int? Age { get; set; }
    public string? SmokerStatus { get; set; }
    public string? Name { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Address { get; set; }

    // ---- Policy ----
    public string? Carrier { get; set; }
    public string? Plan { get; set; }
    public decimal? FaceAmount { get; set; }
    public decimal? Premium { get; set; }
    public string? Email { get; set; }
    public string? Beneficiary { get; set; }
    public string? SecondBeneficiary { get; set; }
    public DateTime? InitialDraftDate { get; set; }
    public DateTime? FutureDraftDate { get; set; }

    // ---- Contact ----
    public string? PhoneNumber { get; set; }
    public string? AltPhone { get; set; }
    public string? PrimaryDoctor { get; set; }

    // ---- Identity (sensitive) ----
    public string? Social { get; set; }
    public string? BornIn { get; set; }
    public string? DriversLicense { get; set; }

    // ---- Physical ----
    public string? Height { get; set; }
    public string? Weight { get; set; }

    // ---- Banking (sensitive) ----
    /// <summary>"Checking" or "Savings".</summary>
    public string? AccountType { get; set; }
    public string? BankName { get; set; }
    public string? AccountNumber { get; set; }
    public string? RoutingNumber { get; set; }

    // ---- Outcome ----
    public CloserStatus CloserStatus { get; set; } = CloserStatus.None;
    public Guid? SubmittedByUserId { get; set; }
    public DateTime? SubmittedAt { get; set; }
    /// <summary>Sale created when the outcome is CompleteAndSold (if any).</summary>
    public Guid? SaleId { get; set; }
}
