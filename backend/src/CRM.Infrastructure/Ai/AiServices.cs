using CRM.Application.Common.Ai;
using CRM.Application.Common.Interfaces;
using CRM.Application.Common.Scoring;
using CRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace CRM.Infrastructure.Ai;

public class AiOptions
{
    public string Provider { get; set; } = "Stub";
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "gpt-4o-mini";
}

/// <summary>
/// Heuristic stub. Production swap: replace with HttpAiCompletionProvider configured for OpenAI/Anthropic/etc.
/// All AI consumers depend on IAiCompletionProvider, so swapping is one DI change.
/// </summary>
public class StubAiCompletionProvider : IAiCompletionProvider
{
    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var trimmed = userPrompt.Length > 240 ? userPrompt.Substring(0, 240) + "…" : userPrompt;
        return Task.FromResult($"[stub-summary] {trimmed}");
    }
}

public class CallSummarizer : ICallSummarizer
{
    private readonly IAiCompletionProvider _ai;
    public CallSummarizer(IAiCompletionProvider ai) => _ai = ai;

    public async Task<CallSummaryResult> SummarizeAsync(string transcriptOrNotes, CancellationToken ct = default)
    {
        const string system = """
            You are a call-center QA assistant. Given a call transcript or agent notes,
            return ONLY a JSON object with this exact shape — no prose before or after:
            {
              "summary": "2-sentence summary of the call",
              "keyMoments": ["...", "...", "..."],
              "disposition": "Interested|NotInterested|CallBack|Sold|Voicemail|NoAnswer|DoNotCall|NotQualified"
            }
            """;
        var raw = await _ai.CompleteAsync(system, transcriptOrNotes, ct);
        var parsed = TryParseJson(raw);
        if (parsed is null)
        {
            // Fallback if model didn't produce JSON
            var sentences = raw.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var summary = string.Join(". ", sentences.Take(2));
            return new CallSummaryResult(summary, sentences.Skip(2).Take(3).ToList(), null);
        }

        var summaryText = parsed.Value.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";
        var moments = parsed.Value.TryGetProperty("keyMoments", out var m) && m.ValueKind == JsonValueKind.Array
            ? m.EnumerateArray().Select(e => e.GetString() ?? "").Where(x => x.Length > 0).ToList()
            : new List<string>();
        var disp = parsed.Value.TryGetProperty("disposition", out var d) ? d.GetString() : null;
        return new CallSummaryResult(summaryText, moments, disp);
    }

    internal static JsonElement? TryParseJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        // Strip ```json fences if the model wrapped its output
        if (s.StartsWith("```"))
        {
            var firstNewline = s.IndexOf('\n');
            if (firstNewline > 0) s = s.Substring(firstNewline + 1);
            if (s.EndsWith("```")) s = s.Substring(0, s.Length - 3);
            s = s.Trim();
        }
        try { return JsonDocument.Parse(s).RootElement.Clone(); }
        catch { return null; }
    }
}

public class LeadAiScorer : ILeadAiScorer
{
    private readonly IAiCompletionProvider _ai;
    public LeadAiScorer(IAiCompletionProvider ai) => _ai = ai;

    public async Task<AiLeadScoreResult> ScoreLeadAsync(IReadOnlyDictionary<string, object?> leadFacts, CancellationToken ct = default)
    {
        const string system = """
            You are a sales-ops assistant scoring leads for an insurance call center.
            Given the lead facts, return ONLY a JSON object with this exact shape — no other prose:
            {
              "score": 0-100,
              "reasoning": "1-2 sentence justification",
              "riskFactors": ["...", "..."]
            }
            Higher score = more likely to convert. Penalize: missing email, missing consent, low-value state, recent contact.
            Reward: jornaya-verified, captured consent, high-value state, fresh lead.
            """;
        var prompt = JsonSerializer.Serialize(leadFacts);
        var raw = await _ai.CompleteAsync(system, prompt, ct);
        var parsed = CallSummarizer.TryParseJson(raw);

        if (parsed is null)
        {
            // Fallback heuristic
            var score = 50;
            if (leadFacts.TryGetValue("jornayaVerified", out var jv) && jv is bool jvb && jvb) score += 20;
            if (leadFacts.TryGetValue("hasEmail", out var he) && he is bool heb && heb) score += 10;
            if (leadFacts.TryGetValue("consentCaptured", out var cc) && cc is bool ccb && ccb) score += 15;
            return new AiLeadScoreResult(Math.Min(100, score), raw, Array.Empty<string>());
        }

        var score2 = parsed.Value.TryGetProperty("score", out var s) && s.TryGetInt32(out var iv) ? iv
            : parsed.Value.TryGetProperty("score", out var s2) && s2.TryGetDouble(out var dv) ? (int)dv : 50;
        var reasoning = parsed.Value.TryGetProperty("reasoning", out var r) ? r.GetString() ?? "" : "";
        var risks = parsed.Value.TryGetProperty("riskFactors", out var rf) && rf.ValueKind == JsonValueKind.Array
            ? rf.EnumerateArray().Select(e => e.GetString() ?? "").Where(x => x.Length > 0).ToList()
            : new List<string>();
        return new AiLeadScoreResult(Math.Clamp(score2, 0, 100), reasoning, risks);
    }
}

public class RecommendationService : IRecommendationService
{
    private readonly AppDbContext _db;
    private readonly ILeadScorer _scorer;
    private readonly IAiCompletionProvider _ai;
    private readonly ILogger<RecommendationService> _logger;

    public RecommendationService(AppDbContext db, ILeadScorer scorer, IAiCompletionProvider ai,
        ILogger<RecommendationService> logger)
    {
        _db = db;
        _scorer = scorer;
        _ai = ai;
        _logger = logger;
    }

    public async Task<RecommendationResult> RecommendForLeadAsync(Guid leadId, CancellationToken ct = default)
    {
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, ct);
        if (lead is null) return new RecommendationResult(Array.Empty<RecommendationItem>());
        var score = await _scorer.ScoreAsync(lead, ct);

        var recs = new List<RecommendationItem>();
        if (score.Score >= 60)
            recs.Add(new RecommendationItem("Schedule callback within 1 hour", "High propensity score", 0.85));
        if (!lead.JornayaVerified && !string.IsNullOrEmpty(lead.JornayaLeadId))
            recs.Add(new RecommendationItem("Run Jornaya verification", "Token present, not yet verified", 0.95));
        if (string.IsNullOrEmpty(lead.Email))
            recs.Add(new RecommendationItem("Capture email on next contact", "Email missing — improves contact rate", 0.7));
        if (lead.Stage == Domain.Enums.WorkflowStage.Followup &&
            lead.UpdatedAt is { } u && u < DateTime.UtcNow.AddDays(-7))
            recs.Add(new RecommendationItem("Mark as Lost or move to Winback", "Stale follow-up > 7 days", 0.6));
        return new RecommendationResult(recs);
    }

    public Task<RecommendationResult> RecommendForAgentAsync(Guid agentId, CancellationToken ct = default) =>
        Task.FromResult(new RecommendationResult(Array.Empty<RecommendationItem>()));
}
