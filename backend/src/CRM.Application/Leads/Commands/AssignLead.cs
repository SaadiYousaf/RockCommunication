using CRM.Application.Common.Assignment;
using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Intake;
using CRM.Application.Leads.Dtos;
using CRM.Domain.Common;
using CRM.Domain.Constants;
using CRM.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Leads.Commands;

public record AssignLeadCommand(Guid LeadId, string TargetRole, string Strategy = "round-robin", Guid? ExplicitUserId = null) : IRequest<LeadDto>;

public class AssignLeadValidator : AbstractValidator<AssignLeadCommand>
{
    public AssignLeadValidator()
    {
        RuleFor(x => x.LeadId).NotEmpty();
        RuleFor(x => x.TargetRole).NotEmpty();
        RuleFor(x => x.Strategy).NotEmpty();
    }
}

public class AssignLeadHandler : IRequestHandler<AssignLeadCommand, LeadDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IAssignmentService _assignment;
    private readonly ICurrentUser _user;
    private readonly IIdentityService _identity;
    private readonly IIntakeNotifier _notifier;

    public AssignLeadHandler(IApplicationDbContext db, IAssignmentService assignment, ICurrentUser user, IIdentityService identity, IIntakeNotifier notifier)
    {
        _db = Guard.AgainstNull(db);
        _assignment = Guard.AgainstNull(assignment);
        _user = Guard.AgainstNull(user);
        _identity = Guard.AgainstNull(identity);
        _notifier = Guard.AgainstNull(notifier);
    }

    public async Task<LeadDto> Handle(AssignLeadCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.AgencyId is null) throw new ForbiddenAccessException();

        var lead = await _db.Leads.FirstOrDefaultAsync(
            l => l.Id == request.LeadId && l.AgencyId == _user.AgencyId, ct)
            ?? throw new NotFoundException(nameof(Lead), request.LeadId);

        if (request.ExplicitUserId is { } uid)
        {
            // The assignee MUST be a real user in the caller's agency — never trust a raw id
            // (otherwise a lead could be "assigned" to a foreign or non-existent user).
            var agencyUserIds = await _identity.ListUserNamesAsync(_user.AgencyId, ct);
            if (!agencyUserIds.ContainsKey(uid))
                throw new NotFoundException("User", uid);
            lead.AssignedUserId = uid;
        }
        else
        {
            await _assignment.AssignAsync(lead, request.TargetRole, request.Strategy, ct);
        }
        await _db.SaveChangesAsync(ct);

        // Tell the assignee a lead just landed in their queue (skip if they assigned it to themselves).
        if (lead.AssignedUserId is { } assignee && assignee != _user.UserId)
            await _notifier.NotifyUserAsync(lead.AgencyId, assignee,
                "New lead assigned to you",
                $"{lead.FirstName} {lead.LastName} — {lead.PhoneNumber} is now in your queue.",
                AppConstants.QueueRoutes.MyQueue, ct);

        return new LeadDto(lead.Id, lead.FirstName, lead.LastName, lead.PhoneNumber,
            lead.Email, lead.State, lead.Stage, lead.Disposition,
            lead.AssignedUserId, lead.TeamId, lead.JornayaVerified, lead.CreatedAt);
    }
}
