using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Infrastructure.Identity;
using EmailOtpCode = CRM.Infrastructure.Identity.EmailOtpCode;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CRM.Infrastructure.Persistence;

public class AppDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>, IApplicationDbContext
{
    private readonly ICurrentUser? _currentUser;

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser) : base(options)
    {
        _currentUser = Guard.AgainstNull(currentUser);
    }

    /// <summary>
    /// Used by the global query filter to scope <see cref="Domain.Common.TenantEntity"/> reads
    /// to the caller's agency. <see cref="Guid.Empty"/> means "no tenant context" — applies to
    /// SuperAdmin requests, design-time, and the seeder; the filter treats Empty as bypass.
    /// </summary>
    public Guid CurrentAgencyId => _currentUser?.AgencyId ?? Guid.Empty;

    /// <summary>
    /// The caller's call center for the finer isolation dimension, or null for an agency-level
    /// user who sees every call center in the agency. Only consulted for <see cref="Domain.Common.CallCenterEntity"/>.
    /// </summary>
    public Guid? CurrentCallCenterId => _currentUser?.CallCenterId;

    /// <summary>
    /// True when the current request context should bypass the tenant filter — and ONLY then:
    /// no user context (seeder / design-time / background jobs, which self-scope by each row's
    /// AgencyId) or SuperAdmin (the one cross-tenant operator role).
    ///
    /// This is FAIL-CLOSED: an authenticated non-SuperAdmin whose agency claim is missing or
    /// empty does NOT bypass. With <see cref="CurrentAgencyId"/> left at <see cref="Guid.Empty"/>,
    /// the filter then matches no rows (sees nothing) rather than every agency's data.
    /// </summary>
    public bool BypassTenantFilter
    {
        get
        {
            if (_currentUser is null) return true;
            // No authenticated principal → seeder, design-time, or background job (no HTTP
            // context). These legitimately operate across agencies and self-scope by each
            // row's own AgencyId, so they bypass. An AUTHENTICATED user never reaches here
            // via this branch, so the fail-closed guarantee below still holds.
            if (!_currentUser.IsAuthenticated) return true;
            if (_currentUser.Roles.Contains(Domain.Enums.Roles.SuperAdmin)) return true;
            return false;
        }
    }

    public DbSet<Agency> Agencies => Set<Agency>();
    public DbSet<CallCenter> CallCenters => Set<CallCenter>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<IpAllowlistEntry> IpAllowlist => Set<IpAllowlistEntry>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<LeadActivity> LeadActivities => Set<LeadActivity>();
    public DbSet<ScheduledCallback> ScheduledCallbacks => Set<ScheduledCallback>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<LeadApplication> LeadApplications => Set<LeadApplication>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<TwoFactorPendingToken> TwoFactorPendingTokens => Set<TwoFactorPendingToken>();
    public DbSet<CommissionEntry> CommissionEntries => Set<CommissionEntry>();
    public DbSet<PayrollRun> PayrollRuns => Set<PayrollRun>();
    public DbSet<QaRubric> QaRubrics => Set<QaRubric>();
    public DbSet<QaRubricItem> QaRubricItems => Set<QaRubricItem>();
    public DbSet<QaReview> QaReviews => Set<QaReview>();
    public DbSet<QaReviewItem> QaReviewItems => Set<QaReviewItem>();
    public DbSet<ChatRoom> ChatRooms => Set<ChatRoom>();
    public DbSet<ChatRoomMember> ChatRoomMembers => Set<ChatRoomMember>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<CallRecord> CallRecords => Set<CallRecord>();
    public DbSet<AgencyCommissionConfig> AgencyCommissionConfigs => Set<AgencyCommissionConfig>();
    public DbSet<Vertical> Verticals => Set<Vertical>();
    public DbSet<Horizontal> Horizontals => Set<Horizontal>();
    public DbSet<EmailOtpCode> EmailOtpCodes => Set<EmailOtpCode>();
    public DbSet<AgentSession> AgentSessions => Set<AgentSession>();
    public DbSet<AgentStatusLog> AgentStatusLogs => Set<AgentStatusLog>();
    public DbSet<WrapUpCode> WrapUpCodes => Set<WrapUpCode>();
    public DbSet<DncEntry> DncEntries => Set<DncEntry>();
    public DbSet<TcpaConsent> TcpaConsents => Set<TcpaConsent>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<LeadSource> LeadSources => Set<LeadSource>();
    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<AgentSkill> AgentSkills => Set<AgentSkill>();
    public DbSet<Script> Scripts => Set<Script>();
    public DbSet<WorkflowRule> WorkflowRules => Set<WorkflowRule>();
    public DbSet<WorkflowAction> WorkflowActions => Set<WorkflowAction>();
    public DbSet<WorkflowExecution> WorkflowExecutions => Set<WorkflowExecution>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<AppModule> AppModules => Set<AppModule>();
    public DbSet<RoleModule> RoleModules => Set<RoleModule>();
    public DbSet<LeadScoringRule> LeadScoringRules => Set<LeadScoringRule>();
    public DbSet<LeadList> LeadLists => Set<LeadList>();
    public DbSet<LeadListMembership> LeadListMemberships => Set<LeadListMembership>();
    public DbSet<LeadImportBatch> LeadImportBatches => Set<LeadImportBatch>();
    public DbSet<Cadence> Cadences => Set<Cadence>();
    public DbSet<CadenceStep> CadenceSteps => Set<CadenceStep>();
    public DbSet<CadenceEnrollment> CadenceEnrollments => Set<CadenceEnrollment>();
    public DbSet<VoicemailAsset> VoicemailAssets => Set<VoicemailAsset>();
    public DbSet<VoicemailDrop> VoicemailDrops => Set<VoicemailDrop>();
    public DbSet<InboundQueue> InboundQueues => Set<InboundQueue>();
    public DbSet<QueuedCall> QueuedCalls => Set<QueuedCall>();
    public DbSet<IvrMenu> IvrMenus => Set<IvrMenu>();
    public DbSet<IvrOption> IvrOptions => Set<IvrOption>();
    public DbSet<KnowledgeArticle> KnowledgeArticles => Set<KnowledgeArticle>();
    public DbSet<PublicLeadCaptureEndpoint> PublicLeadCaptureEndpoints => Set<PublicLeadCaptureEndpoint>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentNote> DocumentNotes => Set<DocumentNote>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Agency>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Code).HasMaxLength(40);
        });

        // Roles can either be global system templates (AgencyId == null) or
        // agency-scoped custom roles (AgencyId set). Two different agencies must be allowed
        // to define a role named "Senior Verifier" without colliding, so the default
        // Identity unique index on NormalizedName is replaced with a composite one.
        b.Entity<ApplicationRole>(e =>
        {
            e.HasIndex(r => r.NormalizedName).HasDatabaseName("RoleNameIndex").IsUnique(false);
            e.HasIndex(r => new { r.NormalizedName, r.AgencyId })
                .HasDatabaseName("IX_AspNetRoles_NormalizedName_AgencyId")
                .IsUnique();
        });

        b.Entity<Team>(e =>
        {
            e.HasIndex(x => new { x.AgencyId, x.Name }).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasOne<Agency>().WithMany(a => a.Teams).HasForeignKey(x => x.AgencyId);
        });

        b.Entity<CallCenter>(e =>
        {
            e.HasIndex(x => new { x.AgencyId, x.Name }).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Code).HasMaxLength(40);
            e.HasOne<Agency>().WithMany(a => a.CallCenters).HasForeignKey(x => x.AgencyId);
        });

        b.Entity<Lead>(e =>
        {
            e.HasIndex(x => new { x.AgencyId, x.PhoneNumber });
            e.HasIndex(x => new { x.AgencyId, x.Stage });
            e.Property(x => x.FirstName).HasMaxLength(80).IsRequired();
            e.Property(x => x.LastName).HasMaxLength(80).IsRequired();
            e.Property(x => x.PhoneNumber).HasMaxLength(20).IsRequired();
            e.HasMany(x => x.Activities).WithOne().HasForeignKey(a => a.LeadId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Callbacks).WithOne().HasForeignKey(c => c.LeadId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Sale).WithOne().HasForeignKey<Sale>(s => s.LeadId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Sale>(e =>
        {
            e.HasIndex(x => new { x.AgencyId, x.SoldAt });
            e.Property(x => x.MonthlyPremium).HasPrecision(18, 2);
            e.Property(x => x.AnnualPremium).HasPrecision(18, 2);
            e.Property(x => x.BankRoutingNumber).HasMaxLength(20);
            e.Property(x => x.BankAccountLast4).HasMaxLength(8);
            e.Property(x => x.BankName).HasMaxLength(120);
            e.Property(x => x.LyonsReference).HasMaxLength(64);
            e.Property(x => x.CoverageApproved).HasPrecision(18, 2);
            e.Property(x => x.PremiumApproved).HasPrecision(18, 2);
            e.Property(x => x.CarrierApproved).HasMaxLength(120);
            e.Property(x => x.PlanApproved).HasMaxLength(120);
            e.Property(x => x.DeclineReason).HasMaxLength(500);
            e.HasIndex(x => new { x.AgencyId, x.ValidatorStatus });
        });

        b.Entity<LeadApplication>(e =>
        {
            e.HasIndex(x => new { x.AgencyId, x.LeadId }).IsUnique();
            e.Property(x => x.FaceAmount).HasPrecision(18, 2);
            e.Property(x => x.Premium).HasPrecision(18, 2);
            // Encrypt the most sensitive PII at rest (SSN + driver's licence) so a stolen DB
            // file / backup doesn't expose them in cleartext. Existing plaintext rows read
            // fine and re-encrypt on their next write.
            var enc = new Security.EncryptedStringConverter();
            e.Property(x => x.Social).HasConversion(enc);
            e.Property(x => x.DriversLicense).HasConversion(enc);
            e.HasOne<Lead>().WithOne(l => l.Application).HasForeignKey<LeadApplication>(x => x.LeadId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RefreshToken>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        });

        b.Entity<TwoFactorPendingToken>(e =>
        {
            e.HasIndex(x => x.Token).IsUnique();
            e.Property(x => x.Token).HasMaxLength(128).IsRequired();
        });

        b.Entity<CommissionEntry>(e =>
        {
            e.HasIndex(x => new { x.AgencyId, x.AgentUserId, x.Paid });
            e.HasIndex(x => x.SaleId);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.RuleName).HasMaxLength(80).IsRequired();
        });

        b.Entity<PayrollRun>(e =>
        {
            e.HasIndex(x => new { x.AgencyId, x.PeriodStart });
            e.Property(x => x.TotalAmount).HasPrecision(18, 2);
            e.Property(x => x.Status).HasMaxLength(40);
        });

        b.Entity<QaRubric>(e =>
        {
            e.HasIndex(x => new { x.AgencyId, x.Name }).IsUnique();
            e.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.RubricId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
        });

        b.Entity<QaReview>(e =>
        {
            e.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.ReviewId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.TotalScore).HasPrecision(10, 2);
            e.Property(x => x.MaxScore).HasPrecision(10, 2);
        });

        b.Entity<ChatRoom>(e =>
        {
            e.HasMany(x => x.Members).WithOne().HasForeignKey(m => m.RoomId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Messages).WithOne().HasForeignKey(m => m.RoomId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        });

        b.Entity<ChatRoomMember>(e =>
        {
            e.HasIndex(x => new { x.RoomId, x.UserId }).IsUnique();
        });

        b.Entity<ChatMessage>(e =>
        {
            e.HasIndex(x => new { x.RoomId, x.SentAt });
            e.Property(x => x.Body).HasMaxLength(4000).IsRequired();
            e.Property(x => x.AttachmentUrl).HasMaxLength(500);
            e.Property(x => x.AttachmentName).HasMaxLength(260);
            e.Property(x => x.AttachmentContentType).HasMaxLength(120);
        });

        b.Entity<Document>(e =>
        {
            e.HasIndex(x => new { x.AgencyId, x.CreatedAt });
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.OriginalFileName).HasMaxLength(260).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(120);
            e.Property(x => x.StorageKey).HasMaxLength(500).IsRequired();
            e.Property(x => x.Kind).HasMaxLength(20);
        });

        b.Entity<DocumentNote>(e =>
        {
            e.HasIndex(x => new { x.DocumentId, x.CreatedAt });
            e.Property(x => x.Body).HasMaxLength(4000).IsRequired();
        });

        b.Entity<AgencyCommissionConfig>(e =>
        {
            e.HasIndex(x => new { x.AgencyId, x.RuleName }).IsUnique();
            e.Property(x => x.RuleName).HasMaxLength(80).IsRequired();
            e.Property(x => x.Amount).HasPrecision(18, 4);
            e.Property(x => x.Threshold).HasPrecision(18, 4);
        });

        b.Entity<Vertical>(e =>
        {
            e.HasIndex(x => new { x.AgencyId, x.Name }).IsUnique();
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
        });

        b.Entity<Horizontal>(e =>
        {
            e.HasIndex(x => new { x.AgencyId, x.Name }).IsUnique();
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
        });

        b.Entity<EmailOtpCode>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserId);
            e.Property(x => x.Code).HasMaxLength(8).IsRequired();
        });

        b.Entity<AgentSession>(e =>
        {
            e.HasIndex(x => new { x.AgencyId, x.UserId, x.ClockInAt });
        });

        b.Entity<AgentStatusLog>(e =>
        {
            e.HasIndex(x => new { x.AgencyId, x.UserId, x.FromAt });
            e.Property(x => x.Reason).HasMaxLength(120);
        });

        b.Entity<WrapUpCode>(e =>
        {
            e.HasIndex(x => new { x.AgencyId, x.Code }).IsUnique();
            e.Property(x => x.Code).HasMaxLength(40).IsRequired();
            e.Property(x => x.Label).HasMaxLength(120).IsRequired();
        });

        b.Entity<DncEntry>(e =>
        {
            e.HasIndex(x => new { x.AgencyId, x.PhoneNormalized }).IsUnique();
            e.Property(x => x.PhoneNormalized).HasMaxLength(20).IsRequired();
            e.Property(x => x.Source).HasMaxLength(40);
        });

        b.Entity<TcpaConsent>(e =>
        {
            e.HasIndex(x => x.LeadId);
            e.Property(x => x.ConsentText).HasMaxLength(4000).IsRequired();
        });

        b.Entity<Campaign>(e =>
        {
            e.HasIndex(x => new { x.AgencyId, x.Code }).IsUnique();
            e.Property(x => x.Code).HasMaxLength(40).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        });

        b.Entity<LeadSource>(e =>
        {
            e.HasIndex(x => new { x.AgencyId, x.Code }).IsUnique();
            e.Property(x => x.Code).HasMaxLength(40).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.CostPerLead).HasPrecision(10, 2);
        });

        b.Entity<Skill>(e =>
        {
            e.HasIndex(x => new { x.AgencyId, x.Code }).IsUnique();
            e.Property(x => x.Code).HasMaxLength(40).IsRequired();
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
        });

        b.Entity<AgentSkill>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.SkillId }).IsUnique();
        });

        b.Entity<WorkflowRule>(e =>
        {
            e.HasIndex(x => new { x.AgencyId, x.EventType, x.IsActive });
            e.HasMany(x => x.Actions).WithOne().HasForeignKey(a => a.RuleId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.EventType).HasMaxLength(60).IsRequired();
        });

        b.Entity<WorkflowAction>(e =>
        {
            e.Property(x => x.ActionType).HasMaxLength(60).IsRequired();
        });

        b.Entity<WorkflowExecution>(e =>
        {
            e.HasIndex(x => new { x.AgencyId, x.RuleId, x.StartedAt });
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.EventType).HasMaxLength(60).IsRequired();
        });

        b.Entity<Permission>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(80).IsRequired();
            e.Property(x => x.Group).HasMaxLength(60);
        });

        b.Entity<RolePermission>(e =>
        {
            e.HasIndex(x => new { x.RoleId, x.PermissionId }).IsUnique();
        });

        b.Entity<AppModule>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(80).IsRequired();
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Group).HasMaxLength(60);
            e.Property(x => x.RoutePath).HasMaxLength(200);
            e.Property(x => x.Icon).HasMaxLength(60);
        });

        b.Entity<RoleModule>(e =>
        {
            e.HasIndex(x => new { x.RoleId, x.ModuleId }).IsUnique();
        });

        b.Entity<LeadScoringRule>(e =>
        {
            e.HasIndex(x => new { x.AgencyId, x.IsActive });
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.FactKey).HasMaxLength(80).IsRequired();
            e.Property(x => x.ComparisonOp).HasMaxLength(10);
        });

        b.Entity<LeadList>(e =>
        {
            e.HasIndex(x => new { x.AgencyId, x.Name }).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        });

        b.Entity<LeadListMembership>(e =>
        {
            e.HasIndex(x => new { x.LeadListId, x.LeadId }).IsUnique();
        });

        b.Entity<LeadImportBatch>(e =>
        {
            e.HasIndex(x => new { x.AgencyId, x.LeadListId, x.CreatedAt });
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.FileName).HasMaxLength(200);
        });

        b.Entity<Cadence>(e =>
        {
            e.HasIndex(x => new { x.AgencyId, x.Name }).IsUnique();
            e.HasMany(x => x.Steps).WithOne().HasForeignKey(s => s.CadenceId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        });

        b.Entity<CadenceStep>(e =>
        {
            e.Property(x => x.StepKind).HasMaxLength(40).IsRequired();
        });

        b.Entity<CadenceEnrollment>(e =>
        {
            e.HasIndex(x => new { x.CadenceId, x.LeadId }).IsUnique();
            e.HasIndex(x => new { x.AgencyId, x.NextRunAt, x.Status });
            e.Property(x => x.Status).HasMaxLength(20);
        });

        b.Entity<VoicemailAsset>(e =>
        {
            e.HasIndex(x => new { x.AgencyId, x.Name }).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Url).HasMaxLength(500).IsRequired();
        });

        b.Entity<VoicemailDrop>(e =>
        {
            e.HasIndex(x => new { x.AgencyId, x.LeadId, x.CreatedAt });
            e.Property(x => x.Status).HasMaxLength(20);
        });

        b.Entity<InboundQueue>(e =>
        {
            e.HasIndex(x => new { x.AgencyId, x.Name }).IsUnique();
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Strategy).HasMaxLength(40);
        });

        b.Entity<QueuedCall>(e =>
        {
            e.HasIndex(x => new { x.AgencyId, x.InboundQueueId, x.Status, x.EnteredAt });
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.Provider).HasMaxLength(40);
            e.Property(x => x.ProviderCallId).HasMaxLength(120);
        });

        b.Entity<IvrMenu>(e =>
        {
            e.HasMany(x => x.Options).WithOne().HasForeignKey(o => o.IvrMenuId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Name).HasMaxLength(120);
        });

        b.Entity<IvrOption>(e =>
        {
            e.Property(x => x.DigitOrSpeech).HasMaxLength(40);
            e.Property(x => x.Label).HasMaxLength(120);
        });

        b.Entity<KnowledgeArticle>(e =>
        {
            e.HasIndex(x => new { x.AgencyId, x.Slug }).IsUnique();
            e.HasIndex(x => new { x.AgencyId, x.Category });
            e.Property(x => x.Slug).HasMaxLength(160).IsRequired();
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Body).HasMaxLength(20000).IsRequired();
        });

        b.Entity<PublicLeadCaptureEndpoint>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
            e.Property(x => x.Slug).HasMaxLength(60).IsRequired();
            e.Property(x => x.SecretHash).HasMaxLength(128);
        });

        b.Entity<Script>(e =>
        {
            e.HasIndex(x => new { x.AgencyId, x.Name, x.Version }).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Body).HasMaxLength(8000).IsRequired();
            e.Property(x => x.Role).HasMaxLength(40);
        });

        b.Entity<CallRecord>(e =>
        {
            e.HasIndex(x => new { x.AgencyId, x.LeadId });
            e.HasIndex(x => x.ProviderCallId);
            e.Property(x => x.Provider).HasMaxLength(40);
            e.Property(x => x.ProviderCallId).HasMaxLength(120);
            e.Property(x => x.Status).HasMaxLength(40);
        });

        b.Entity<AuditEntry>(e =>
        {
            e.HasIndex(x => new { x.EntityName, x.EntityId });
            e.Property(x => x.EntityName).HasMaxLength(120).IsRequired();
            e.Property(x => x.EntityId).HasMaxLength(64).IsRequired();
            e.Property(x => x.Action).HasMaxLength(40).IsRequired();
        });

        // Global query filters:
        //   * BaseEntity   → IsDeleted == false
        //   * TenantEntity → IsDeleted == false AND (BypassTenantFilter OR AgencyId == CurrentAgencyId)
        //
        // For TenantEntity we use the generic SetTenantFilter<T> method so the lambda
        // closes over `this` properly — EF Core treats the DbContext member references
        // (BypassTenantFilter, CurrentAgencyId) as query parameters re-evaluated per request.
        var setTenantFilter = typeof(AppDbContext).GetMethod(
            nameof(SetTenantFilter),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var setCallCenterFilter = typeof(AppDbContext).GetMethod(
            nameof(SetCallCenterFilter),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        foreach (var entityType in b.Model.GetEntityTypes())
        {
            var clr = entityType.ClrType;
            if (!typeof(Domain.Common.BaseEntity).IsAssignableFrom(clr)) continue;

            // CallCenterEntity is the most specific — it derives from TenantEntity, so this
            // check MUST come first. It layers the call-center dimension on top of agency.
            if (typeof(Domain.Common.CallCenterEntity).IsAssignableFrom(clr))
            {
                setCallCenterFilter.MakeGenericMethod(clr).Invoke(this, new object[] { b });
            }
            else if (typeof(Domain.Common.TenantEntity).IsAssignableFrom(clr))
            {
                setTenantFilter.MakeGenericMethod(clr).Invoke(this, new object[] { b });
            }
            else
            {
                var param = System.Linq.Expressions.Expression.Parameter(clr, "e");
                var notDeleted = System.Linq.Expressions.Expression.Equal(
                    System.Linq.Expressions.Expression.Property(param, nameof(Domain.Common.BaseEntity.IsDeleted)),
                    System.Linq.Expressions.Expression.Constant(false));
                b.Entity(clr).HasQueryFilter(System.Linq.Expressions.Expression.Lambda(notDeleted, param));
            }
        }
    }

    private void SetTenantFilter<T>(ModelBuilder b) where T : Domain.Common.TenantEntity
    {
        b.Entity<T>().HasQueryFilter(e =>
            !e.IsDeleted &&
            (BypassTenantFilter || e.AgencyId == CurrentAgencyId));
    }

    /// <summary>
    /// Two-level isolation for call-center-scoped entities: the row must be in the caller's
    /// agency AND (the caller is agency-level — <see cref="CurrentCallCenterId"/> is null, so
    /// they see every call center — OR the row's call center matches the caller's). SuperAdmin /
    /// no-user context still bypasses via <see cref="BypassTenantFilter"/>.
    /// </summary>
    private void SetCallCenterFilter<T>(ModelBuilder b) where T : Domain.Common.CallCenterEntity
    {
        b.Entity<T>().HasQueryFilter(e =>
            !e.IsDeleted &&
            (BypassTenantFilter ||
             (e.AgencyId == CurrentAgencyId &&
              (CurrentCallCenterId == null || e.CallCenterId == CurrentCallCenterId))));
    }
}
