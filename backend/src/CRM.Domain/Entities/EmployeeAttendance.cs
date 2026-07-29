using CRM.Domain.Common;
using CRM.Domain.Enums;

namespace CRM.Domain.Entities;

/// <summary>
/// One employee's attendance for one day. HR marks it (or it's derived from clock-in); the
/// monthly roll-up of these statuses feeds payroll (present / absent / late / half / leave / NCNS).
/// Unique per (Employee, Date). Agency-scoped.
/// </summary>
public class EmployeeAttendance : TenantEntity
{
    public Guid EmployeeId { get; set; }
    /// <summary>The calendar day (date component only, UTC).</summary>
    public DateTime Date { get; set; }
    public AttendanceStatus Status { get; set; } = AttendanceStatus.Present;
    public string? Notes { get; set; }
}
