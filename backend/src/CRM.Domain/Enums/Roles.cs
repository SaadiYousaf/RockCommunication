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

    public const string Followups = "Followups";
    public const string Correspondence = "Correspondence";
    public const string Winbacks = "Winbacks";

    public static readonly string[] All =
    {
        SuperAdmin,
        Admin, CEO, QAManager, ProjectManager, TechLead,
        CallCenterAdmin,
        ProgramManager, TeamLead,
        Fronter, Verifier,
        JrCloser, Closer, Validator, SelfValidator,
        Followups, Correspondence, Winbacks
    };

    /// <summary>
    /// Roles for which two-factor authentication is mandatory — they hold cross-tenant or
    /// agency-wide privileges, so 2FA is enforced (see TwoFactorSetupRequiredMiddleware).
    /// </summary>
    public static readonly string[] RequireTwoFactor = { SuperAdmin, Admin, CEO };

    /// <summary>True if any of the given roles makes two-factor authentication mandatory.</summary>
    public static bool TwoFactorMandatory(IEnumerable<string> roles) =>
        roles.Any(r => RequireTwoFactor.Contains(r, StringComparer.OrdinalIgnoreCase));
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
