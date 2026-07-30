namespace CRM.Domain.Common;

/// <summary>
/// Canonical commission rule names. Each string is the contract between the commission engine
/// (which emits <c>CommissionLine.RuleName</c>) and every reader that de-duplicates, pays, or
/// configures those lines — and it is persisted in <c>CommissionEntry.RuleName</c> and the
/// per-agency commission config. If any value drifts, commissions silently duplicate or stop
/// matching, so there is exactly one home for them here, referenced everywhere.
/// </summary>
public static class CommissionRuleNames
{
    public const string CloserFlatRate = "closer-flat-rate";
    public const string JrCloserSplit = "jr-closer-split";
    public const string ValidatorBonus = "validator-bonus";
    public const string LicenseAgentApproval = "license-agent-approval";
    public const string HighPremiumKicker = "high-premium-kicker";
    public const string TeamLeadOverride = "team-lead-override";
}
