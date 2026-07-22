using CRM.Application.Common.Ai;
using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Ai;

public record SummarizeCallCommand(Guid CallId) : IRequest<CallSummaryResult>;

public class SummarizeCallHandler : IRequestHandler<SummarizeCallCommand, CallSummaryResult>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly ICallSummarizer _summarizer;

    public SummarizeCallHandler(IApplicationDbContext db, ICurrentUser user, ICallSummarizer summarizer)
    {
        _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); _summarizer = Guard.AgainstNull(summarizer);
    }

    public async Task<CallSummaryResult> Handle(SummarizeCallCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        var call = await _db.CallRecords.FirstOrDefaultAsync(
            c => c.Id == request.CallId && c.AgencyId == _user.AgencyId, ct);
        if (call is null) throw new NotFoundException("Call", request.CallId);
        var input = call.Notes ?? $"Call {call.ProviderCallId} {call.Status}, agent {call.AgentUserId}, lead {call.LeadId}";
        return await _summarizer.SummarizeAsync(input, ct);
    }
}

public record AiScoreLeadQuery(Guid LeadId) : IRequest<AiLeadScoreResult>;

public class AiScoreLeadHandler : IRequestHandler<AiScoreLeadQuery, AiLeadScoreResult>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly ILeadAiScorer _scorer;

    public AiScoreLeadHandler(IApplicationDbContext db, ICurrentUser user, ILeadAiScorer scorer)
    {
        _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); _scorer = Guard.AgainstNull(scorer);
    }

    public async Task<AiLeadScoreResult> Handle(AiScoreLeadQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        var lead = await _db.Leads.FirstOrDefaultAsync(
            l => l.Id == request.LeadId && l.AgencyId == _user.AgencyId, ct);
        if (lead is null) throw new NotFoundException("Lead", request.LeadId);

        var facts = new Dictionary<string, object?>
        {
            ["state"] = lead.State, ["source"] = lead.Source,
            ["jornayaVerified"] = lead.JornayaVerified,
            ["consentCaptured"] = lead.ConsentCaptured,
            ["hasEmail"] = !string.IsNullOrEmpty(lead.Email),
            ["heuristicScore"] = lead.Score
        };
        return await _scorer.ScoreLeadAsync(facts, ct);
    }
}

public record RecommendForLeadQuery(Guid LeadId) : IRequest<RecommendationResult>;

public class RecommendForLeadHandler : IRequestHandler<RecommendForLeadQuery, RecommendationResult>
{
    private readonly IRecommendationService _recs;
    public RecommendForLeadHandler(IRecommendationService recs) => _recs = Guard.AgainstNull(recs);

    public Task<RecommendationResult> Handle(RecommendForLeadQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        return _recs.RecommendForLeadAsync(request.LeadId, ct);
    }
}
