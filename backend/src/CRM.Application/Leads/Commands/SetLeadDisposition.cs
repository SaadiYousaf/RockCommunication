using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Leads.Dtos;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Leads.Commands;

/// <summary>
/// Records the outcome (disposition) of the last contact attempt WITHOUT changing the lead's
/// workflow stage. The stage machine (TransitionLeadCommand) never allows a same-stage move, so
/// disposition changes need their own path — otherwise setting a disposition 409s.
/// </summary>
public record SetLeadDispositionCommand(Guid LeadId, LeadDisposition Disposition, string? Notes = null) : IRequest<LeadDto>;

public class SetLeadDispositionValidator : AbstractValidator<SetLeadDispositionCommand>
{
    public SetLeadDispositionValidator()
    {
        RuleFor(x => x.LeadId).NotEmpty();
        RuleFor(x => x.Disposition).IsInEnum();
    }
}

public class SetLeadDispositionHandler : IRequestHandler<SetLeadDispositionCommand, LeadDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public SetLeadDispositionHandler(IApplicationDbContext db, ICurrentUser user)
    {
        _db = Guard.AgainstNull(db);
        _user = Guard.AgainstNull(user);
    }

    public async Task<LeadDto> Handle(SetLeadDispositionCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null || _user.AgencyId is null) throw new ForbiddenAccessException();

        var lead = await _db.Leads.FirstOrDefaultAsync(
            l => l.Id == request.LeadId && l.AgencyId == _user.AgencyId, ct)
            ?? throw new NotFoundException(nameof(Lead), request.LeadId);

        // Log the disposition on the timeline as a same-stage activity so history is preserved.
        _db.LeadActivities.Add(new LeadActivity
        {
            AgencyId = lead.AgencyId, CallCenterId = lead.CallCenterId,
            LeadId = lead.Id,
            UserId = _user.UserId.Value,
            FromStage = lead.Stage,
            ToStage = lead.Stage,
            Disposition = request.Disposition,
            Notes = request.Notes
        });

        lead.Disposition = request.Disposition;
        lead.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return new LeadDto(lead.Id, lead.FirstName, lead.LastName, lead.PhoneNumber,
            lead.Email, lead.State, lead.Stage, lead.Disposition,
            lead.AssignedUserId, lead.TeamId, lead.JornayaVerified, lead.CreatedAt);
    }
}
