using CRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Agency> Agencies { get; }
    DbSet<CRM.Domain.Entities.CallCenter> CallCenters { get; }
    DbSet<Team> Teams { get; }
    DbSet<IpAllowlistEntry> IpAllowlist { get; }
    DbSet<Lead> Leads { get; }
    DbSet<LeadActivity> LeadActivities { get; }
    DbSet<ScheduledCallback> ScheduledCallbacks { get; }
    DbSet<Sale> Sales { get; }
    DbSet<LeadApplication> LeadApplications { get; }
    DbSet<AuditEntry> AuditEntries { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<CommissionEntry> CommissionEntries { get; }
    DbSet<PayrollRun> PayrollRuns { get; }
    DbSet<QaRubric> QaRubrics { get; }
    DbSet<QaRubricItem> QaRubricItems { get; }
    DbSet<QaReview> QaReviews { get; }
    DbSet<QaReviewItem> QaReviewItems { get; }
    DbSet<ChatRoom> ChatRooms { get; }
    DbSet<ChatRoomMember> ChatRoomMembers { get; }
    DbSet<ChatMessage> ChatMessages { get; }
    DbSet<CallRecord> CallRecords { get; }
    DbSet<AgencyCommissionConfig> AgencyCommissionConfigs { get; }
    DbSet<Vertical> Verticals { get; }
    DbSet<Horizontal> Horizontals { get; }
    DbSet<AgentSession> AgentSessions { get; }
    DbSet<AgentStatusLog> AgentStatusLogs { get; }
    DbSet<WrapUpCode> WrapUpCodes { get; }
    DbSet<DncEntry> DncEntries { get; }
    DbSet<TcpaConsent> TcpaConsents { get; }
    DbSet<Campaign> Campaigns { get; }
    DbSet<LeadSource> LeadSources { get; }
    DbSet<Skill> Skills { get; }
    DbSet<AgentSkill> AgentSkills { get; }
    DbSet<Script> Scripts { get; }
    DbSet<WorkflowRule> WorkflowRules { get; }
    DbSet<WorkflowAction> WorkflowActions { get; }
    DbSet<WorkflowExecution> WorkflowExecutions { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<AppModule> AppModules { get; }
    DbSet<RoleModule> RoleModules { get; }
    DbSet<LeadScoringRule> LeadScoringRules { get; }
    DbSet<LeadList> LeadLists { get; }
    DbSet<LeadListMembership> LeadListMemberships { get; }
    DbSet<LeadImportBatch> LeadImportBatches { get; }
    DbSet<Cadence> Cadences { get; }
    DbSet<CadenceStep> CadenceSteps { get; }
    DbSet<CadenceEnrollment> CadenceEnrollments { get; }
    DbSet<VoicemailAsset> VoicemailAssets { get; }
    DbSet<VoicemailDrop> VoicemailDrops { get; }
    DbSet<InboundQueue> InboundQueues { get; }
    DbSet<QueuedCall> QueuedCalls { get; }
    DbSet<IvrMenu> IvrMenus { get; }
    DbSet<IvrOption> IvrOptions { get; }
    DbSet<KnowledgeArticle> KnowledgeArticles { get; }
    DbSet<PublicLeadCaptureEndpoint> PublicLeadCaptureEndpoints { get; }
    DbSet<Document> Documents { get; }
    DbSet<DocumentNote> DocumentNotes { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ICurrentUser
{
    Guid? UserId { get; }
    string? UserName { get; }
    Guid? AgencyId { get; }
    /// <summary>
    /// The caller's call center, or null for an agency-level user who sees every call
    /// center in the agency. Drives the second isolation dimension in the query filter.
    /// </summary>
    Guid? CallCenterId { get; }
    IReadOnlyList<string> Roles { get; }
    bool IsAuthenticated { get; }
    string? IpAddress { get; }
}

public interface IJwtTokenService
{
    /// <summary>
    /// Issues an access+refresh token pair. <paramref name="extraClaims"/> lets callers
    /// attach short-lived state (e.g. <c>pwd_change=true</c>) the consumer can gate on.
    /// </summary>
    Task<TokenResult> IssueAsync(
        Guid userId, string userName, Guid agencyId, IEnumerable<string> roles,
        Guid? callCenterId = null,
        IReadOnlyDictionary<string, string>? extraClaims = null,
        CancellationToken ct = default);

    Task<TokenResult?> RefreshAsync(string refreshToken, CancellationToken ct = default);
    Task RevokeAsync(string refreshToken, CancellationToken ct = default);
    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default);
}

public record TokenResult(string AccessToken, string RefreshToken, DateTime ExpiresAt);

public interface ITwoFactorService
{
    string GenerateSecret();
    string BuildQrUri(string userEmail, string secret, string issuer = "CRM");
    bool Verify(string secret, string code);
}
