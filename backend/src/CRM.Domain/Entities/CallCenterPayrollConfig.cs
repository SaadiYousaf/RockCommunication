using CRM.Domain.Common;

namespace CRM.Domain.Entities;

/// <summary>
/// Per-call-centre rules for how attendance-driven salary deductions are calculated. Each call
/// centre's admin can tune these; payroll auto-fills the deduction amounts from them.
///
/// A "day's pay" is <c>BasicSalary / WorkingDays</c>. Half-day / absent / NCNS deductions are a
/// configurable multiple of a day's pay; late-coming is a flat fine per occurrence (late penalties
/// are usually a fixed amount, not a fraction of salary).
/// </summary>
public class CallCenterPayrollConfig : TenantEntity
{
    public Guid CallCenterId { get; set; }

    /// <summary>Flat amount deducted per late-coming (currency units, e.g. PKR).</summary>
    public decimal LateComingFine { get; set; } = PayrollConfigDefaults.LateComingFine;

    /// <summary>Fraction of a day's pay deducted per half-day (default 0.5 = half a day).</summary>
    public decimal HalfDayFactor { get; set; } = PayrollConfigDefaults.HalfDayFactor;

    /// <summary>Fraction of a day's pay deducted per absent day (default 1.0 = a full day).</summary>
    public decimal AbsentDayFactor { get; set; } = PayrollConfigDefaults.AbsentDayFactor;

    /// <summary>Fraction of a day's pay deducted per NCNS (default 2.0 — an unexcused no-show is penalised more).</summary>
    public decimal NcnsFactor { get; set; } = PayrollConfigDefaults.NcnsFactor;
}

/// <summary>Sensible starting rules used when a call centre hasn't configured its own yet.</summary>
public static class PayrollConfigDefaults
{
    public const decimal LateComingFine = 0m;
    public const decimal HalfDayFactor = 0.5m;
    public const decimal AbsentDayFactor = 1.0m;
    public const decimal NcnsFactor = 2.0m;
}
