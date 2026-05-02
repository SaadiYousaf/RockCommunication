using CRM.Domain.Entities;

namespace CRM.Application.Common.Scoring;

public record ScoringContext(Lead Lead, IReadOnlyDictionary<string, object?> Facts);
public record ScoringResult(int Score, IReadOnlyList<(string Rule, int Points, string? Note)> Breakdown);

public interface IScoringRule
{
    string Name { get; }
    int Priority { get; }
    Task<(int Points, string? Note)> ScoreAsync(ScoringContext ctx, CancellationToken ct = default);
}

public interface ILeadScorer
{
    Task<ScoringResult> ScoreAsync(Lead lead, CancellationToken ct = default);
}
