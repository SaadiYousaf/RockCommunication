using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Integrations;
using CRM.Application.Common.Interfaces;
using CRM.Application.Common.Scoring;
using CRM.Application.Leads.Dtos;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Leads.Commands;

public record VerifyJornayaCommand(Guid LeadId) : IRequest<LeadDto>;

public class VerifyJornayaHandler : IRequestHandler<VerifyJornayaCommand, LeadDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IJornayaProvider _jornaya;
    private readonly ICurrentUser _user;
    private readonly ILeadScorer _scorer;

    public VerifyJornayaHandler(IApplicationDbContext db, IJornayaProvider jornaya, ICurrentUser user, ILeadScorer scorer)
    {
        _db = Guard.AgainstNull(db);
        _jornaya = Guard.AgainstNull(jornaya);
        _user = Guard.AgainstNull(user);
        _scorer = Guard.AgainstNull(scorer);
    }

    public async Task<LeadDto> Handle(VerifyJornayaCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.AgencyId is null) throw new ForbiddenAccessException();

        var lead = await _db.Leads.FirstOrDefaultAsync(
            l => l.Id == request.LeadId && l.AgencyId == _user.AgencyId, ct)
            ?? throw new NotFoundException(nameof(Lead), request.LeadId);

        var result = await _jornaya.VerifyAsync(lead.Id.ToString(), lead.JornayaLeadId, ct);
        lead.JornayaVerified = result.Verified;
        lead.JornayaVerifiedAt = result.VerifiedAt;
        // Record who ran the verification so the UI can show "Verified by …" and lock the button.
        lead.JornayaVerifiedBy = result.Verified ? _user.UserName : null;

        // Jornaya verification is a scoring input (+20). Recompute and persist the denormalized
        // Score now so the stored value the leads list / queue read stays in sync with the live
        // breakdown shown on the detail page — otherwise the header score goes stale after verify.
        var scoring = await _scorer.ScoreAsync(lead, ct);
        lead.Score = scoring.Score;
        await _db.SaveChangesAsync(ct);

        return new LeadDto(lead.Id, lead.FirstName, lead.LastName, lead.PhoneNumber,
            lead.Email, lead.State, lead.Stage, lead.Disposition,
            lead.AssignedUserId, lead.TeamId, lead.JornayaVerified, lead.CreatedAt);
    }
}
