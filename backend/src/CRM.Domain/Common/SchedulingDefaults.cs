namespace CRM.Domain.Common;

/// <summary>
/// Single home for the scheduler/meetings module's magic values — default duration, field
/// length caps, and the email subject prefixes. Referenced by the EF entity configuration
/// (max-lengths), the FluentValidation validators, and the invite email builder so none of
/// these numbers/strings are ever inlined twice and drift apart.
/// </summary>
public static class SchedulingDefaults
{
    /// <summary>Default length of a new meeting when only a start time is picked (minutes).</summary>
    public const int DefaultDurationMinutes = 30;

    // ── Field length caps (mirrored by the EF column max-lengths + validators) ──
    public const int MaxTitleLength = 200;
    public const int MaxDescriptionLength = 4000;
    public const int MaxLocationLength = 300;
    public const int MaxOnlineUrlLength = 1000;
    /// <summary>RFC-5321 caps an address at 254 chars; round up for the column.</summary>
    public const int MaxEmailLength = 320;
    /// <summary>Guardrail on how many people one meeting can invite in a single request.</summary>
    public const int MaxAttendees = 100;

    // ── Email subject prefixes ─────────────────────────────────────────────────
    public const string InviteSubjectPrefix = "Invitation: ";
    public const string CancellationSubjectPrefix = "Cancelled: ";

    /// <summary>Friendly product name rendered in invite emails (kept out of provider options
    /// so the Application layer needn't depend on Infrastructure email configuration).</summary>
    public const string ProductName = "Rock Communication CRM";
}
