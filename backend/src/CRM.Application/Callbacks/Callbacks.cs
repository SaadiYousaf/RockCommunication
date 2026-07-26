using CRM.Application.Common.Exceptions;
using DomainRoles = CRM.Domain.Enums.Roles;
using CRM.Application.Common.Interfaces;
using CRM.Application.Intake;
using CRM.Domain.Common;
using CRM.Domain.Constants;
using CRM.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Callbacks;

public record ScheduleCallbackDto(Guid LeadId, DateTime ScheduledFor, string? Reason);
public record CallbackDto(Guid Id, Guid LeadId, string LeadName, string LeadPhone, Guid AssignedUserId, DateTime ScheduledFor, string? Reason, bool Completed);

public record ScheduleCallbackCommand(ScheduleCallbackDto Input) : IRequest<CallbackDto>;

public class ScheduleCallbackValidator : AbstractValidator<ScheduleCallbackCommand>
{
    public ScheduleCallbackValidator()
    {
        RuleFor(x => x.Input.LeadId).NotEmpty();
        RuleFor(x => x.Input.ScheduledFor).GreaterThan(DateTime.UtcNow.AddMinutes(-1));
    }
}

public class ScheduleCallbackHandler : IRequestHandler<ScheduleCallbackCommand, CallbackDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IIntakeNotifier _notifier;

    public ScheduleCallbackHandler(IApplicationDbContext db, ICurrentUser user, IIntakeNotifier notifier)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
        _notifier = Guard.AgainstNull(notifier);
    }

    public async Task<CallbackDto> Handle(ScheduleCallbackCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null || _user.AgencyId is null) throw new ForbiddenAccessException();

        var input = request.Input;
        var lead = await _db.Leads.FirstOrDefaultAsync(
            l => l.Id == input.LeadId && l.AgencyId == _user.AgencyId, ct)
            ?? throw new NotFoundException(nameof(Lead), input.LeadId);

        var cb = new ScheduledCallback
        {
            AgencyId = lead.AgencyId, CallCenterId = lead.CallCenterId,
            LeadId = lead.Id,
            AssignedUserId = lead.AssignedUserId ?? _user.UserId.Value,
            ScheduledFor = input.ScheduledFor,
            Reason = input.Reason
        };
        _db.ScheduledCallbacks.Add(cb);
        await _db.SaveChangesAsync(ct);

        // If the callback is assigned to someone OTHER than the person scheduling it, tell them now
        // (they otherwise only find out via the 15-minutes-before reminder job).
        if (cb.AssignedUserId != _user.UserId.Value)
            await _notifier.NotifyUserAsync(lead.AgencyId, cb.AssignedUserId,
                "Callback assigned to you",
                $"Call back {lead.FirstName} {lead.LastName} on {input.ScheduledFor.ToLocalTime():g}.",
                AppConstants.QueueRoutes.Callbacks, ct);

        return new CallbackDto(cb.Id, cb.LeadId, $"{lead.FirstName} {lead.LastName}".Trim(), lead.PhoneNumber,
            cb.AssignedUserId, cb.ScheduledFor, cb.Reason, cb.Completed);
    }
}

public record CompleteCallbackCommand(Guid Id) : IRequest<CallbackDto>;

public class CompleteCallbackHandler : IRequestHandler<CompleteCallbackCommand, CallbackDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public CompleteCallbackHandler(IApplicationDbContext db, ICurrentUser user)
    {
        _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user);
    }

    public async Task<CallbackDto> Handle(CompleteCallbackCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        var cb = await _db.ScheduledCallbacks.FirstOrDefaultAsync(
            x => x.Id == request.Id && x.AgencyId == _user.AgencyId, ct)
            ?? throw new NotFoundException(nameof(ScheduledCallback), request.Id);
        // A callback is a per-agent task — only its owner (or a manager) may complete it, so one
        // agent can't silently clear another agent's follow-up out of their queue.
        var isManager = _user.Roles.Contains(DomainRoles.TeamLead)
            || _user.Roles.Contains(DomainRoles.ProgramManager)
            || _user.Roles.Contains(DomainRoles.Admin)
            || _user.Roles.Contains(DomainRoles.SuperAdmin);
        if (cb.AssignedUserId != _user.UserId && !isManager)
            throw new ForbiddenAccessException("You can only complete your own callbacks.");
        cb.Completed = true;
        await _db.SaveChangesAsync(ct);
        var lead = await _db.Leads.AsNoTracking().FirstOrDefaultAsync(l => l.Id == cb.LeadId, ct);
        return new CallbackDto(cb.Id, cb.LeadId,
            lead is null ? "—" : $"{lead.FirstName} {lead.LastName}".Trim(), lead?.PhoneNumber ?? "—",
            cb.AssignedUserId, cb.ScheduledFor, cb.Reason, cb.Completed);
    }
}

public record MyCallbacksQuery(bool IncludeCompleted = false) : IRequest<IReadOnlyList<CallbackDto>>;

public class MyCallbacksHandler : IRequestHandler<MyCallbacksQuery, IReadOnlyList<CallbackDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public MyCallbacksHandler(IApplicationDbContext db, ICurrentUser user)
    {
        _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user);
    }

    public async Task<IReadOnlyList<CallbackDto>> Handle(MyCallbacksQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null || _user.AgencyId is null) throw new ForbiddenAccessException();
        var q = _db.ScheduledCallbacks.Where(c => c.AgencyId == _user.AgencyId && c.AssignedUserId == _user.UserId);
        if (!request.IncludeCompleted) q = q.Where(c => !c.Completed);
        var cbs = await q.OrderBy(c => c.ScheduledFor).ToListAsync(ct);

        // Resolve the lead's name + phone so the queue shows WHO to call — not a raw GUID. One query,
        // dictionary lookup with a fallback so a callback isn't dropped if its lead was removed.
        var leadIds = cbs.Select(c => c.LeadId).Distinct().ToList();
        var byId = (await _db.Leads.AsNoTracking().Where(l => leadIds.Contains(l.Id))
                .Select(l => new { l.Id, l.FirstName, l.LastName, l.PhoneNumber }).ToListAsync(ct))
            .ToDictionary(l => l.Id);
        return cbs.Select(c =>
        {
            byId.TryGetValue(c.LeadId, out var l);
            return new CallbackDto(c.Id, c.LeadId,
                l is null ? "—" : $"{l.FirstName} {l.LastName}".Trim(), l?.PhoneNumber ?? "—",
                c.AssignedUserId, c.ScheduledFor, c.Reason, c.Completed);
        }).ToList();
    }
}
