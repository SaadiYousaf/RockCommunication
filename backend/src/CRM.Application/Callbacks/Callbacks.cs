using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Callbacks;

public record ScheduleCallbackDto(Guid LeadId, DateTime ScheduledFor, string? Reason);
public record CallbackDto(Guid Id, Guid LeadId, Guid AssignedUserId, DateTime ScheduledFor, string? Reason, bool Completed);

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

    public ScheduleCallbackHandler(IApplicationDbContext db, ICurrentUser user)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
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
            AgencyId = lead.AgencyId,
            LeadId = lead.Id,
            AssignedUserId = lead.AssignedUserId ?? _user.UserId.Value,
            ScheduledFor = input.ScheduledFor,
            Reason = input.Reason
        };
        _db.ScheduledCallbacks.Add(cb);
        await _db.SaveChangesAsync(ct);
        return new CallbackDto(cb.Id, cb.LeadId, cb.AssignedUserId, cb.ScheduledFor, cb.Reason, cb.Completed);
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
        cb.Completed = true;
        await _db.SaveChangesAsync(ct);
        return new CallbackDto(cb.Id, cb.LeadId, cb.AssignedUserId, cb.ScheduledFor, cb.Reason, cb.Completed);
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
        return await q.OrderBy(c => c.ScheduledFor)
            .Select(c => new CallbackDto(c.Id, c.LeadId, c.AssignedUserId, c.ScheduledFor, c.Reason, c.Completed))
            .ToListAsync(ct);
    }
}
