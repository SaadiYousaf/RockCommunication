using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Sales.Commands;

public record PayrollRunDto(Guid Id, DateTime PeriodStart, DateTime PeriodEnd, decimal TotalAmount, string Status, DateTime? ProcessedAt);

public record CreatePayrollRunCommand(DateTime PeriodStart, DateTime PeriodEnd) : IRequest<PayrollRunDto>;

public class CreatePayrollRunValidator : AbstractValidator<CreatePayrollRunCommand>
{
    public CreatePayrollRunValidator()
    {
        RuleFor(x => x.PeriodEnd).GreaterThan(x => x.PeriodStart);
    }
}

public class CreatePayrollRunHandler : IRequestHandler<CreatePayrollRunCommand, PayrollRunDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    public CreatePayrollRunHandler(IApplicationDbContext db, ICurrentUser user) { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); }

    public async Task<PayrollRunDto> Handle(CreatePayrollRunCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null || _user.AgencyId is null) throw new ForbiddenAccessException();

        var run = new PayrollRun
        {
            AgencyId = _user.AgencyId.Value,
            PeriodStart = request.PeriodStart,
            PeriodEnd = request.PeriodEnd,
            TotalAmount = 0m,               // set once we know exactly what this run claimed
            Status = "Processed",
            ProcessedAt = DateTime.UtcNow,
            ProcessedByUserId = _user.UserId.Value
        };
        _db.PayrollRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        // Atomically CLAIM every still-unpaid entry in the period for THIS run in one conditional
        // UPDATE. Because it filters on !Paid (and SQLite serialises writes), a second concurrent
        // payroll run finds nothing left to claim — preventing the double-payout TOCTOU.
        // Make the period end inclusive of the whole PeriodEnd day: the UI sends `to` as a
        // date-only UTC midnight, so a raw `< PeriodEnd` would drop everything earned that last
        // day and never pay it (matches the CSV-export / MyCommissions rollover).
        var periodEndExclusive = request.PeriodEnd.Date.AddDays(1);
        await _db.CommissionEntries
            .Where(c => c.AgencyId == _user.AgencyId && !c.Paid
                        && c.EarnedAt >= request.PeriodStart && c.EarnedAt < periodEndExclusive)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Paid, true)
                .SetProperty(c => c.PaidAt, run.ProcessedAt)
                .SetProperty(c => c.PayrollRunId, run.Id), ct);

        // Total = exactly the entries this run claimed (summed in memory — SQLite has no decimal SUM).
        var claimed = await _db.CommissionEntries
            .Where(c => c.PayrollRunId == run.Id).Select(c => c.Amount).ToListAsync(ct);
        run.TotalAmount = claimed.Sum();
        await _db.SaveChangesAsync(ct);

        return new PayrollRunDto(run.Id, run.PeriodStart, run.PeriodEnd, run.TotalAmount, run.Status, run.ProcessedAt);
    }
}

public record ListPayrollRunsQuery() : IRequest<IReadOnlyList<PayrollRunDto>>;
public class ListPayrollRunsHandler : IRequestHandler<ListPayrollRunsQuery, IReadOnlyList<PayrollRunDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    public ListPayrollRunsHandler(IApplicationDbContext db, ICurrentUser user) { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); }

    public async Task<IReadOnlyList<PayrollRunDto>> Handle(ListPayrollRunsQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        return await _db.PayrollRuns
            .Where(r => r.AgencyId == _user.AgencyId)
            .OrderByDescending(r => r.PeriodStart)
            .Select(r => new PayrollRunDto(r.Id, r.PeriodStart, r.PeriodEnd, r.TotalAmount, r.Status, r.ProcessedAt))
            .ToListAsync(ct);
    }
}

public record MyCommissionsQuery(DateTime From, DateTime To, bool? Paid = null) : IRequest<IReadOnlyList<CommissionEntryDto>>;
public record CommissionEntryDto(Guid Id, Guid SaleId, Guid AgentUserId, string RuleName, decimal Amount, string? Note, DateTime EarnedAt, bool Paid);

public class MyCommissionsHandler : IRequestHandler<MyCommissionsQuery, IReadOnlyList<CommissionEntryDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    public MyCommissionsHandler(IApplicationDbContext db, ICurrentUser user) { _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); }

    public async Task<IReadOnlyList<CommissionEntryDto>> Handle(MyCommissionsQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null || _user.AgencyId is null) throw new ForbiddenAccessException();
        var q = _db.CommissionEntries.Where(c => c.AgencyId == _user.AgencyId
            && c.AgentUserId == _user.UserId
            && c.EarnedAt >= request.From && c.EarnedAt < request.To);
        if (request.Paid is { } p) q = q.Where(c => c.Paid == p);
        return await q.OrderByDescending(c => c.EarnedAt)
            .Select(c => new CommissionEntryDto(c.Id, c.SaleId, c.AgentUserId, c.RuleName, c.Amount, c.Note, c.EarnedAt, c.Paid))
            .ToListAsync(ct);
    }
}
