using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Hr;

// ── DTOs ────────────────────────────────────────────────────────────────────

/// <summary>One employee's mark for a chosen day (Status null = not yet marked).</summary>
public record AttendanceRowDto(
    Guid EmployeeId, string FullName, string AgentCode, EmployeeDesignation Designation,
    Guid? CallCenterId, string? CallCenterName, AttendanceStatus? Status, string? Notes);

/// <summary>One employee's monthly attendance roll-up (feeds payroll).</summary>
public record AttendanceSummaryRowDto(
    Guid EmployeeId, string FullName, string AgentCode, EmployeeDesignation Designation,
    Guid? CallCenterId, string? CallCenterName,
    int Present, int Absent, int Late, int HalfDay, int Leave, int Ncns, int Marked);

// ── Requests ────────────────────────────────────────────────────────────────

public record MarkAttendanceCommand(Guid EmployeeId, DateTime Date, AttendanceStatus Status, string? Notes = null)
    : IRequest<Unit>;

public record AttendanceForDateQuery(DateTime Date, Guid? CallCenterId = null)
    : IRequest<IReadOnlyList<AttendanceRowDto>>;

public record AttendanceSummaryQuery(int Year, int Month, Guid? CallCenterId = null)
    : IRequest<IReadOnlyList<AttendanceSummaryRowDto>>;

// ── Handlers ────────────────────────────────────────────────────────────────

public class AttendanceHandlers :
    IRequestHandler<MarkAttendanceCommand, Unit>,
    IRequestHandler<AttendanceForDateQuery, IReadOnlyList<AttendanceRowDto>>,
    IRequestHandler<AttendanceSummaryQuery, IReadOnlyList<AttendanceSummaryRowDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public AttendanceHandlers(IApplicationDbContext db, ICurrentUser user)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
    }

    public async Task<Unit> Handle(MarkAttendanceCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        HrAccess.EnsureHr(_user);
        var day = request.Date.Date;
        var emp = await _db.Employees.FirstOrDefaultAsync(x => x.Id == request.EmployeeId, ct)
            ?? throw new NotFoundException(nameof(Employee), request.EmployeeId);
        var existing = await _db.EmployeeAttendances
            .FirstOrDefaultAsync(a => a.EmployeeId == request.EmployeeId && a.Date == day, ct);
        if (existing is null)
        {
            _db.EmployeeAttendances.Add(new EmployeeAttendance
            {
                AgencyId = emp.AgencyId, EmployeeId = emp.Id, Date = day,
                Status = request.Status, Notes = Clean(request.Notes),
            });
        }
        else
        {
            existing.Status = request.Status;
            existing.Notes = Clean(request.Notes);
            existing.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }

    public async Task<IReadOnlyList<AttendanceRowDto>> Handle(AttendanceForDateQuery request, CancellationToken ct)
    {
        HrAccess.EnsureHr(_user);
        var day = request.Date.Date;
        var employees = await EmployeesQuery(request.CallCenterId).ToListAsync(ct);
        var empIds = employees.Select(e => e.Id).ToList();
        var marks = await _db.EmployeeAttendances.AsNoTracking()
            .Where(a => a.Date == day && empIds.Contains(a.EmployeeId))
            .ToDictionaryAsync(a => a.EmployeeId, ct);
        var ccNames = await CallCenterNamesAsync(employees.Select(e => e.CallCenterId), ct);
        return employees.Select(e =>
        {
            marks.TryGetValue(e.Id, out var m);
            return new AttendanceRowDto(e.Id, e.FullName, e.AgentCode, e.Designation, e.CallCenterId,
                NameOrNull(ccNames, e.CallCenterId), m?.Status, m?.Notes);
        }).ToList();
    }

    public async Task<IReadOnlyList<AttendanceSummaryRowDto>> Handle(AttendanceSummaryQuery request, CancellationToken ct)
    {
        HrAccess.EnsureHr(_user);
        var start = new DateTime(request.Year, request.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMonths(1);
        var employees = await EmployeesQuery(request.CallCenterId).ToListAsync(ct);
        var empIds = employees.Select(e => e.Id).ToList();
        var marks = await _db.EmployeeAttendances.AsNoTracking()
            .Where(a => a.Date >= start && a.Date < end && empIds.Contains(a.EmployeeId))
            .Select(a => new { a.EmployeeId, a.Status }).ToListAsync(ct);
        var byEmp = marks.GroupBy(m => m.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());
        var ccNames = await CallCenterNamesAsync(employees.Select(e => e.CallCenterId), ct);
        return employees.Select(e =>
        {
            byEmp.TryGetValue(e.Id, out var ms);
            ms ??= new();
            int Count(AttendanceStatus s) => ms.Count(m => m.Status == s);
            return new AttendanceSummaryRowDto(e.Id, e.FullName, e.AgentCode, e.Designation, e.CallCenterId,
                NameOrNull(ccNames, e.CallCenterId),
                Count(AttendanceStatus.Present), Count(AttendanceStatus.Absent), Count(AttendanceStatus.Late),
                Count(AttendanceStatus.HalfDay), Count(AttendanceStatus.Leave), Count(AttendanceStatus.Ncns), ms.Count);
        }).ToList();
    }

    private IQueryable<Employee> EmployeesQuery(Guid? callCenterId)
    {
        var q = _db.Employees.AsNoTracking().AsQueryable();
        if (callCenterId is { } cc) q = q.Where(e => e.CallCenterId == cc);
        return q.OrderBy(e => e.FullName);
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private async Task<Dictionary<Guid, string>> CallCenterNamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var set = ids.Where(x => x is { } g && g != Guid.Empty).Select(x => x!.Value).Distinct().ToList();
        if (set.Count == 0) return new();
        return (await _db.CallCenters.AsNoTracking().IgnoreQueryFilters()
                .Where(c => set.Contains(c.Id)).Select(c => new { c.Id, c.Name }).ToListAsync(ct))
            .ToDictionary(c => c.Id, c => c.Name);
    }

    private static string? NameOrNull(Dictionary<Guid, string> names, Guid? id) =>
        id is { } g && names.TryGetValue(g, out var n) ? n : null;
}
