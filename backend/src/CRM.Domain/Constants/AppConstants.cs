namespace CRM.Domain.Constants;

/// <summary>
/// Consolidated string/route constants that were previously scattered as inline literals.
/// Grouped by concern. (Role names, permissions, module codes, banking codes, JWT claim names
/// and SignalR agent-event names keep their existing bounded-context homes — Roles, Permissions,
/// Modules, BankingPolicy, CustomJwtClaims, AgentEvents — and are extended there, not duplicated.)
/// </summary>
public static class AppConstants
{
    /// <summary>Values persisted to <c>Lead.Source</c>. Casing is significant (stored data).</summary>
    public static class LeadSources
    {
        public const string FronterIntake = "Fronter Intake";
        public const string CloserIntake = "Closer Intake";
        public const string Verifier = "Verifier";
        public const string Closer = "Closer";
        public const string InboundCall = "InboundCall";
        public const string Internal = "Internal";
    }

    /// <summary>SPA deep-link routes used as notification URLs — must match the frontend router.</summary>
    public static class QueueRoutes
    {
        public const string VerifyQueue = "/verify-queue";
        public const string CloseQueue = "/close-queue";
        public const string ValidateQueue = "/validate-queue";
    }

    /// <summary>Direction values persisted on <c>CallRecord.Direction</c>.</summary>
    public static class CallDirection
    {
        public const string Inbound = "Inbound";
        public const string Outbound = "Outbound";
    }

    /// <summary>Custom HTTP header names used by webhooks and outbound integrations.</summary>
    public static class HttpHeaderNames
    {
        /// <summary>HMAC signature header on inbound webhooks.</summary>
        public const string Signature = "X-Signature";
        /// <summary>API-key header on outbound integration calls.</summary>
        public const string ApiKey = "X-Api-Key";
    }
}
