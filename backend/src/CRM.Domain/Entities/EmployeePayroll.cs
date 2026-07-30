using CRM.Domain.Common;

namespace CRM.Domain.Entities;

/// <summary>
/// One employee's payroll for one month. Attendance counts are derived from
/// <see cref="EmployeeAttendance"/>; monthly commission from the closer's CommissionEntries;
/// the money components are entered by HR. Basic salary + advance carry forward from the prior
/// month (an advance paid last month is deducted this month). Unique per (Employee, Year, Month).
/// </summary>
public class EmployeePayroll : TenantEntity
{
    public Guid EmployeeId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }

    // ── Earnings ──────────────────────────────────────────────────────────────
    public decimal BasicSalary { get; set; }
    public decimal Punctuality { get; set; }
    public decimal DailyBonus { get; set; }
    public decimal MonthlyCommissions { get; set; }
    public decimal TransportAllowance { get; set; }
    public decimal SpecialAllowance { get; set; }

    // ── Deductions ────────────────────────────────────────────────────────────
    /// <summary>Advance salary to recover this month (carried from last month's advance).</summary>
    public decimal AdvanceSalary { get; set; }
    public decimal Docks { get; set; }

    // Money withheld for each attendance-driven line — the "amount against" its day count.
    public decimal LateComingAmount { get; set; }
    public decimal HalfDaysAmount { get; set; }
    public decimal AbsentDaysAmount { get; set; }
    public decimal NcnsAmount { get; set; }

    // ── Attendance counts (the "number" against each line) ────────────────────
    public int WorkingDays { get; set; }
    public int PresentDays { get; set; }
    public int LeavesApproved { get; set; }
    public int LateComing { get; set; }
    public int HalfDays { get; set; }
    public int AbsentDays { get; set; }
    public int Ncns { get; set; }

    public string? Notes { get; set; }
    /// <summary>Set once HR locks the month (kept editable until then).</summary>
    public bool Finalized { get; set; }
}
