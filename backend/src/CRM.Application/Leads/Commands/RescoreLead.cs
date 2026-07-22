using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Common.Scoring;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Leads.Commands;

public record LeadScoreDto(int Score, IReadOnlyList<LeadScoreLineDto> Breakdown);
public record LeadScoreLineDto(string Rule, int Points, string? Note);

public record RescoreLeadCommand(Guid LeadId) : IRequest<LeadScoreDto>;

public class RescoreLeadHandler : IRequestHandler<RescoreLeadCommand, LeadScoreDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly ILeadScorer _scorer;

    public RescoreLeadHandler(IApplicationDbContext db, ICurrentUser user, ILeadScorer scorer)
    {
        _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); _scorer = Guard.AgainstNull(scorer);
    }

    public async Task<LeadScoreDto> Handle(RescoreLeadCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        var lead = await _db.Leads.FirstOrDefaultAsync(
            l => l.Id == request.LeadId && l.AgencyId == _user.AgencyId, ct)
            ?? throw new NotFoundException(nameof(Lead), request.LeadId);

        var score = await _scorer.ScoreAsync(lead, ct);
        lead.Score = score.Score;
        await _db.SaveChangesAsync(ct);

        return new LeadScoreDto(score.Score,
            score.Breakdown.Select(b => new LeadScoreLineDto(b.Rule, b.Points, b.Note)).ToList());
    }
}
