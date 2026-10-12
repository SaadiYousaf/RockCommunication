using CRM.Domain.Common;

namespace CRM.Domain.Entities;

/// <summary>
/// One person's end-of-day reflection, asked when they sign out: what they learned today, and
/// whether they feel they did justice with the day.
///
/// Answering is OPTIONAL — the sign-out dialog carries a Skip button and nobody is held in the
/// application by it. That is deliberate: a mandatory box at the end of a shift collects "n/a"
/// typed to make the screen go away, which is worse than no answer at all because it reads like
/// a real one.
///
/// Stored one row per person per working day, so a second sign-out later the same day updates
/// that day's answer rather than filing a duplicate. A blank field on the second pass leaves the
/// earlier answer standing — signing out at lunch and again at six should not erase what somebody
/// wrote in the morning.
/// </summary>
public class ShiftReflection : TenantEntity
{
    public Guid UserId { get; set; }

    /// <summary>The working day being reflected on (date component only, UTC).</summary>
    public DateTime ShiftDate { get; set; }

    /// <summary>"What new thing did you learn regarding your job today?" — free text, may be null.</summary>
    public string? Learned { get; set; }

    /// <summary>
    /// "Do you think you did justice with your job today?" — null when they skipped the question.
    /// Null and false are different answers and must not be collapsed: one is silence, the other is
    /// somebody telling their manager the day did not go well.
    /// </summary>
    public bool? DidJustice { get; set; }

    /// <summary>Optional context for the answer above, in their own words.</summary>
    public string? JusticeNote { get; set; }

    /// <summary>
    /// The author's call centre, captured at sign-out so a floor manager can read their own floor.
    /// Nullable because agency-level staff (Admin, CEO, licence agents) belong to no call centre —
    /// a non-nullable column here is exactly what made agency staff unreachable in payroll.
    /// </summary>
    public Guid? CallCenterId { get; set; }
}
