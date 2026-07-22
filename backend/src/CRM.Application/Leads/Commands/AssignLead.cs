using CRM.Application.Common.Assignment;
using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Leads.Dtos;
using CRM.Domain.Common;
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

    public AssignLeadHandler(IApplicationDbContext db, IAssignmentService assignment, ICurrentUser user)
    {
        _db = Guard.AgainstNull(db);
        _assignment = Guard.AgainstNull(assignment);
        _user = Guard.AgainstNull(user);
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
            lead.AssignedUserId = uid;
        }
        else
        {
            await _assignment.AssignAsync(lead, request.TargetRole, request.Strategy, ct);
        }
        await _db.SaveChangesAsync(ct);

        return new LeadDto(lead.Id, lead.FirstName, lead.LastName, lead.PhoneNumber,
            lead.Email, lead.State, lead.Stage, lead.Disposition,
            lead.AssignedUserId, lead.TeamId, lead.JornayaVerified, lead.CreatedAt);
    }
}
