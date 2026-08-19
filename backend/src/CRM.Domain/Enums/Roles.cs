using System;
using System.Collections.Generic;
using System.Linq;

namespace CRM.Domain.Enums;

public static class Roles
{
    // Global / cross-tenant
    public const string SuperAdmin = "SuperAdmin";

    // Agency-scoped leadership
    public const string Admin = "Admin";
    /// <summary>Agency CEO — owns one agency; may create/manage that agency's call centers.</summary>
    public const string CEO = "CEO";
    public const string QAManager = "QAManager";
    public const string ProjectManager = "ProjectManager";
    public const string TechLead = "TechLead";

    /// <summary>
    /// Call-center scoped administrator — manages the users and the profile of their own
    /// call center only. Cannot create agencies or call centers.
    /// </summary>
    public const string CallCenterAdmin = "CallCenterAdmin";

    // Existing
    public const string ProgramManager = "ProgramManager";
    public const string TeamLead = "TeamLead";

    public const string Fronter = "Fronter";
    public const string Verifier = "Verifier";

    public const string JrCloser = "JrCloser";
    public const string Closer = "Closer";
    public const string Validator = "Validator";
    public const string SelfValidator = "SelfValidator";

    /// <summary>
    /// Agency-level licensed agent. Belongs to an agency (CallCenterId = null) and can be
    /// assigned a sale by a Submission Agent at approval; earns commission on assignment.
    /// </summary>
    public const string LicenseAgent = "LicenseAgent";

    public const string Followups = "Followups";
    public const string Correspondence = "Correspondence";
    public const string Winbacks = "Winbacks";

    /// <summary>
    /// Retention agent — works submitted policies that went bad post-submission (bank rejected,
    /// payment bounced/NSF, client cancelled, declined, application error) and tries to recover
    /// them. Sees ONLY policies in those problem statuses, within their agency/call-center scope.
    /// </summary>
    public const string Retention = "Retention";

    // ── Back-office / support ─────────────────────────────────────────────────
    /// <summary>Human-resources staff: manage employee records, attendance, payroll, hiring.</summary>
    public const string HR = "HR";
    /// <summary>Support staff (office boy) — an employee record with no pipeline duties.</summary>
    public const string OfficeBoy = "OfficeBoy";

    public static readonly string[] All =
    {
        SuperAdmin,
        Admin, CEO, QAManager, ProjectManager, TechLead,
        CallCenterAdmin,
        ProgramManager, TeamLead,
        Fronter, Verifier,
        JrCloser, Closer, Validator, SelfValidator,
        LicenseAgent,
        Followups, Correspondence, Winbacks,
        Retention,
        HR, OfficeBoy
    };

    /// <summary>
    /// Roles for which two-factor authentication is mandatory (see TwoFactorSetupRequiredMiddleware).
    /// SuperAdmin and Admin remain excluded <b>by explicit user request</b> — do not re-add without
    /// asking. The CEO already required it; ProgramManager and CallCenterAdmin are added here because
    /// they are admin-equivalent (they hold <c>users.manage</c> and can reshape their part of the org),
    /// so cross-user power without 2FA was the real audit gap. Central (cross-agency) Submission Agents
    /// are also forced, handled via <see cref="IsCentralSubmissionAgent"/>.
    /// </summary>
    public static readonly string[] RequireTwoFactor = { CEO, ProgramManager, CallCenterAdmin };

    /// <summary>
    /// Agency-admin-equivalent roles. Handing one of these out is a privilege escalation, so only
    /// SuperAdmin / Admin / CEO may grant them. Enforced on BOTH the create (register) and the
    /// update-roles paths so a plain <c>users.manage</c> holder can never mint agency-admin power.
    /// </summary>
    public static readonly string[] Elevated = { Admin, CEO, ProgramManager, CallCenterAdmin };

    /// <summary>True if the given roles include an agency-admin-equivalent (elevated) role.</summary>
    public static bool GrantsElevated(IEnumerable<string> roles) =>
        roles.Any(r => Elevated.Contains(r, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Only SuperAdmin / Admin / CEO are permitted to hand out <see cref="Elevated"/> roles.
    /// </summary>
    public static bool CanGrantElevated(IEnumerable<string> callerRoles) =>
        callerRoles.Any(r => string.Equals(r, SuperAdmin, StringComparison.OrdinalIgnoreCase)
                          || string.Equals(r, Admin, StringComparison.OrdinalIgnoreCase)
                          || string.Equals(r, CEO, StringComparison.OrdinalIgnoreCase));

    /// <summary>True if any of the given roles makes two-factor authentication mandatory.</summary>
    public static bool TwoFactorMandatory(IEnumerable<string> roles) =>
        roles.Any(r => RequireTwoFactor.Contains(r, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Authority tier for a single role — higher outranks lower. Used to enforce "manage only
    /// below your own rank" for sensitive user operations (e.g. an admin resetting a password may
    /// act on juniors but not a peer admin or the CEO). A user's rank is the MAX across their roles.
    /// </summary>
    public static int RankOfRole(string role) => role switch
    {
        SuperAdmin => 100,
        CEO => 80,
        Admin => 80,
        ProgramManager => 60,
        CallCenterAdmin => 60,
        TeamLead => 40,
        _ => 10,            // agents (Fronter/Closer/Validator/…) and everyone else
    };

    /// <summary>The caller's effective authority tier — the highest rank among all their roles.</summary>
    public static int RankOf(IEnumerable<string> roles) =>
        roles.Select(RankOfRole).DefaultIfEmpty(0).Max();

    /// <summary>
    /// A "central" (SMH-level) Submission Agent: holds the <see cref="Validator"/> role but is
    /// bound to no agency (empty/none). These validate and approve sales across ALL agencies and
    /// read cross-tenant PII, so 2FA is mandatory for them (unlike agency-scoped validators).
    /// </summary>
    public static bool IsCentralSubmissionAgent(Guid? agencyId, IEnumerable<string> roles) =>
        (agencyId is null || agencyId == Guid.Empty) &&
        roles.Contains(Validator, StringComparer.OrdinalIgnoreCase);
}

public enum WorkflowStage
{
    New = 0,
    Fronted = 10,
    Verified = 20,
    JrClosed = 30,
    Closed = 40,
    Validated = 50,
    Funded = 60,
    Followup = 70,
    Winback = 80,
    Lost = 90
}

public enum AgentStatus
{
    Offline = 0,
    Available = 1,
    OnCall = 2,
    Break = 3,
    Lunch = 4,
    Training = 5,
    Meeting = 6
}

public enum LeadDisposition
{
    None = 0,
    Interested = 1,
    NotInterested = 2,
    CallBack = 3,
    DoNotCall = 4,
    Sold = 5,
    NotQualified = 6,
    Voicemail = 7,
    NoAnswer = 8,
    WrongNumber = 9
}
