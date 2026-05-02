namespace CRM.Application.Common.Ai;

public record CallSummaryResult(string Summary, IReadOnlyList<string> KeyMoments, string? RecommendedDisposition);
public record AiLeadScoreResult(int Score, string Reasoning, IReadOnlyList<string> RiskFactors);
public record RecommendationResult(IReadOnlyList<RecommendationItem> Items);
public record RecommendationItem(string Action, string Reason, double Confidence);

public interface ICallSummarizer
{
    Task<CallSummaryResult> SummarizeAsync(string transcriptOrNotes, CancellationToken ct = default);
}

public interface ILeadAiScorer
{
    Task<AiLeadScoreResult> ScoreLeadAsync(IReadOnlyDictionary<string, object?> leadFacts, CancellationToken ct = default);
}

public interface IRecommendationService
{
    Task<RecommendationResult> RecommendForLeadAsync(Guid leadId, CancellationToken ct = default);
    Task<RecommendationResult> RecommendForAgentAsync(Guid agentId, CancellationToken ct = default);
}

public interface IAiCompletionProvider
{
    /// <summary>Provider-agnostic completion call. Returns model output text.</summary>
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
}
