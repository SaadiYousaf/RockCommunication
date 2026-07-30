using CRM.Domain.Common;
using CRM.Domain.Enums;

namespace CRM.Domain.Entities;

/// <summary>
/// A recruitment / interview record for a job candidate. Agency-scoped. CNIC is encrypted at
/// rest. When a candidate is hired, HR can convert them into an <see cref="Employee"/>.
/// </summary>
public class Interview : TenantEntity
{
    public DateTime? InterviewDate { get; set; }
    public string CandidateName { get; set; } = string.Empty;
    /// <summary>National ID (CNIC). Encrypted at rest.</summary>
    public string? Cnic { get; set; }
    public string? PhoneNumber { get; set; }
    public string? PositionAppliedFor { get; set; }
    /// <summary>Free-text experience summary, e.g. "3 years — outbound sales".</summary>
    public string? Experience { get; set; }
    public decimal? ExpectedSalary { get; set; }
    public DateTime? AbleToJoinOn { get; set; }
    public OfferStatus Status { get; set; } = OfferStatus.Applied;
    public string? PositionOffered { get; set; }
    public decimal? SalaryOffered { get; set; }
    public string? Remarks { get; set; }
    /// <summary>Scheduled onboarding/training date-time (surfaces reminders).</summary>
    public DateTime? TrainingScheduledAt { get; set; }

    /// <summary>When the automatic "training coming up" reminder was last sent, so the background
    /// job fires at most once per scheduled training. Reset to null whenever the training is
    /// rescheduled so a fresh reminder goes out for the new time.</summary>
    public DateTime? TrainingReminderSentAt { get; set; }
}
