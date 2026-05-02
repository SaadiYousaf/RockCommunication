using System.Text.Json;

namespace CRM.Infrastructure.Workflow;

/// <summary>
/// Evaluates a JSON condition tree against an event's fact dictionary.
/// Grammar:
///   { "all": [ ...conds ] }
///   { "any": [ ...conds ] }
///   { "fact": "score", "op": ">=", "value": 50 }
/// Operators: eq, ne, gt, gte, lt, lte, in, notIn, contains, isNull, isNotNull
/// </summary>
public class RuleConditionEvaluator
{
    public static bool Evaluate(string? json, IReadOnlyDictionary<string, object?> facts)
    {
        if (string.IsNullOrWhiteSpace(json)) return true;
        using var doc = JsonDocument.Parse(json);
        return EvaluateNode(doc.RootElement, facts);
    }

    private static bool EvaluateNode(JsonElement node, IReadOnlyDictionary<string, object?> facts)
    {
        if (node.ValueKind != JsonValueKind.Object) return false;

        if (node.TryGetProperty("all", out var allArr))
            return allArr.EnumerateArray().All(c => EvaluateNode(c, facts));
        if (node.TryGetProperty("any", out var anyArr))
            return anyArr.EnumerateArray().Any(c => EvaluateNode(c, facts));
        if (!node.TryGetProperty("fact", out var factElem)) return false;

        var key = factElem.GetString() ?? "";
        var op = node.TryGetProperty("op", out var opElem) ? (opElem.GetString() ?? "eq") : "eq";
        facts.TryGetValue(key, out var actual);
        var compare = node.TryGetProperty("value", out var v) ? v : default;

        return op.ToLowerInvariant() switch
        {
            "isnull" => actual is null,
            "isnotnull" => actual is not null,
            "eq" => Equal(actual, compare),
            "ne" => !Equal(actual, compare),
            "gt" => Compare(actual, compare) > 0,
            "gte" => Compare(actual, compare) >= 0,
            "lt" => Compare(actual, compare) < 0,
            "lte" => Compare(actual, compare) <= 0,
            "in" => InArray(actual, compare),
            "notin" => !InArray(actual, compare),
            "contains" => actual is string s && compare.ValueKind == JsonValueKind.String &&
                          s.Contains(compare.GetString() ?? "", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool Equal(object? a, JsonElement b)
    {
        if (a is null) return b.ValueKind == JsonValueKind.Null;
        return b.ValueKind switch
        {
            JsonValueKind.String => string.Equals(a.ToString(), b.GetString(), StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Number => decimal.TryParse(a.ToString(), out var da) && b.TryGetDecimal(out var db) && da == db,
            JsonValueKind.True => a is bool ab && ab,
            JsonValueKind.False => a is bool ab2 && !ab2,
            _ => false
        };
    }

    private static int Compare(object? a, JsonElement b)
    {
        if (a is null) return -1;
        if (decimal.TryParse(a.ToString(), out var da) && b.TryGetDecimal(out var db))
            return da.CompareTo(db);
        return string.CompareOrdinal(a.ToString(), b.GetString());
    }

    private static bool InArray(object? a, JsonElement b)
    {
        if (b.ValueKind != JsonValueKind.Array) return false;
        return b.EnumerateArray().Any(item => Equal(a, item));
    }
}
