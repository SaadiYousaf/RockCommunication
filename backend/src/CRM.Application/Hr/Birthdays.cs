using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Hr;

public record UpcomingBirthdayDto(
    Guid EmployeeId, string FullName, string AgentCode, DateTime DateOfBirth, int DaysUntil, bool IsToday);

public record UpcomingBirthdaysQuery(int Days = 30) : IRequest<IReadOnlyList<UpcomingBirthdayDto>>;

public class UpcomingBirthdaysHandler : IRequestHandler<UpcomingBirthdaysQuery, IReadOnlyList<UpcomingBirthdayDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public UpcomingBirthdaysHandler(IApplicationDbContext db, ICurrentUser user)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
    }

    public async Task<IReadOnlyList<UpcomingBirthdayDto>> Handle(UpcomingBirthdaysQuery request, CancellationToken ct)
    {
        HrAccess.EnsureHr(_user);
        var today = DateTime.UtcNow.Date;
        var employees = await _db.Employees.AsNoTracking()
            .Where(e => e.DateOfBirth != null)
            .Select(e => new { e.Id, e.FullName, e.AgentCode, Dob = e.DateOfBirth!.Value })
            .ToListAsync(ct);

        var list = new List<UpcomingBirthdayDto>();
        foreach (var e in employees)
        {
            var next = NextBirthday(e.Dob, today);
            var daysUntil = (next - today).Days;
            if (daysUntil <= request.Days)
                list.Add(new UpcomingBirthdayDto(e.Id, e.FullName, e.AgentCode, e.Dob, daysUntil, daysUntil == 0));
        }
        return list.OrderBy(x => x.DaysUntil).ThenBy(x => x.FullName).ToList();
    }

    /// <summary>The next occurrence of a date-of-birth on/after today, clamping Feb-29 to Feb-28.</summary>
    private static DateTime NextBirthday(DateTime dob, DateTime today)
    {
        DateTime Occurrence(int year) =>
            new(year, dob.Month, Math.Min(dob.Day, DateTime.DaysInMonth(year, dob.Month)));
        var thisYear = Occurrence(today.Year);
        return thisYear < today ? Occurrence(today.Year + 1) : thisYear;
    }
}
