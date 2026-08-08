using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Sales.Queries;

public record PayrollExportRow(string AgentUserName, string AgentEmail, string RuleName, decimal Amount, DateTime EarnedAt, bool Paid);

public record ExportPayrollQuery(Guid? RunId, DateTime? From, DateTime? To)
    : IRequest<IReadOnlyList<PayrollExportRow>>;

public class ExportPayrollHandler : IRequestHandler<ExportPayrollQuery, IReadOnlyList<PayrollExportRow>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IIdentityService _identity;

    public ExportPayrollHandler(IApplicationDbContext db, ICurrentUser user, IIdentityService identity)
    {
        _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); _identity = Guard.AgainstNull(identity);
    }

    public async Task<IReadOnlyList<PayrollExportRow>> Handle(ExportPayrollQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        // Authorization is the controller's [HasPermission(PayrollProcess)] gate — don't re-gate on a
        // hard-coded role list here (it wrongly 403'd a CEO, who holds PayrollProcess).

        // Agency-wide export (mirrors CreatePayrollRun's agency-wide claim): bypass the call-centre
        // filter so a call-centre-pinned processor's CSV isn't silently missing other centres' entries
        // (making the row-sum disagree with PayrollRun.TotalAmount). Re-add tenant + soft-delete.
        var q = _db.CommissionEntries.IgnoreQueryFilters()
            .Where(c => c.AgencyId == _user.AgencyId && !c.IsDeleted);
        if (request.RunId is { } r) q = q.Where(c => c.PayrollRunId == r);
        if (request.From is { } f) q = q.Where(c => c.EarnedAt >= f);
        if (request.To is { } t) q = q.Where(c => c.EarnedAt < t);

        var entries = await q.OrderBy(c => c.EarnedAt).ToListAsync(ct);

        var users = await _identity.ListUsersAsync(_user.AgencyId, ct);
        var byId = users.ToDictionary(u => u.Id);

        return entries.Select(c =>
        {
            byId.TryGetValue(c.AgentUserId, out var u);
            return new PayrollExportRow(u?.UserName ?? c.AgentUserId.ToString(), u?.Email ?? "",
                c.RuleName, c.Amount, c.EarnedAt, c.Paid);
        }).ToList();
    }
}
