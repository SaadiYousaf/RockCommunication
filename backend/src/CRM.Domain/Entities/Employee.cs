using CRM.Domain.Common;
using CRM.Domain.Enums;

namespace CRM.Domain.Entities;

/// <summary>
/// An HR employee record — the master profile for a person who works at the agency (whether or
/// not they hold a system login). Agency-scoped; <see cref="CallCenterId"/> is a grouping field
/// (HR sees the whole agency), not a query-filter dimension. CNIC and bank account number are
/// encrypted at rest (EncryptedStringConverter, same AES-GCM as SSN) — see AppDbContext.
/// </summary>
public class Employee : TenantEntity
{
    // ── Identity ──────────────────────────────────────────────────────────────
    public string FullName { get; set; } = string.Empty;
    /// <summary>Human-friendly "Agent ID" assigned by HR (e.g. EMP-014). Unique per agency.</summary>
    public string AgentCode { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? OfficialEmail { get; set; }
    public EmployeeDesignation Designation { get; set; } = EmployeeDesignation.Other;
    /// <summary>Call centre the employee belongs to (for grouping attendance/payroll); null = agency-level.</summary>
    public Guid? CallCenterId { get; set; }
    /// <summary>Optional link to the person's app login account (attendance / payroll tie back to it).</summary>
    public Guid? UserId { get; set; }

    // ── Personal ──────────────────────────────────────────────────────────────
    /// <summary>National ID (CNIC). Encrypted at rest.</summary>
    public string? Cnic { get; set; }
    public Gender Gender { get; set; } = Gender.Male;
    public MaritalStatus MaritalStatus { get; set; } = MaritalStatus.Single;
    /// <summary>Drives birthday notifications.</summary>
    public DateTime? DateOfBirth { get; set; }
    /// <summary>Year we last sent this person's birthday notification (dedupe — one per year).</summary>
    public int? LastBirthdayNotifiedYear { get; set; }

    // ── Guardian / emergency contact ──────────────────────────────────────────
    public string? GuardianPhone { get; set; }
    public GuardianRelationship? GuardianRelationship { get; set; }

    // ── Addresses ─────────────────────────────────────────────────────────────
    public string? CurrentAddress { get; set; }
    public string? PermanentAddress { get; set; }

    // ── Employment ────────────────────────────────────────────────────────────
    public DateTime? DateOfJoining { get; set; }
    public EmploymentStatus EmploymentStatus { get; set; } = EmploymentStatus.Probation;
    /// <summary>Manager this employee reports to (self-reference); resolved to a name at query time.</summary>
    public Guid? ReportingToEmployeeId { get; set; }
    /// <summary>Free-text shift, e.g. "9:00 AM – 6:00 PM".</summary>
    public string? WorkHours { get; set; }

    // ── Banking ───────────────────────────────────────────────────────────────
    public string? BankName { get; set; }
    public string? BankAccountTitle { get; set; }
    /// <summary>Bank account number. Encrypted at rest.</summary>
    public string? BankAccountNumber { get; set; }
    public string? BankIban { get; set; }

    // ── Uploaded documents (IFileStorage keys) ────────────────────────────────
    public string? PhotoKey { get; set; }
    public string? IdCardFrontKey { get; set; }
    public string? IdCardBackKey { get; set; }
}
