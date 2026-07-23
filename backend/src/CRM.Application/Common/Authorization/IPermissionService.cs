namespace CRM.Application.Common.Authorization;

/// <summary>
/// Canonical permission codes. Convention: "<area>.<verb>". Read codes gate viewing,
/// write codes gate any mutation (create/update/delete/move/assign/etc.) inside that area.
/// New codes added here are auto-seeded into the Permissions table on next boot.
/// </summary>
public static class Permissions
{
    // Pipeline
    public const string LeadsRead = "leads.read";
    public const string LeadsWrite = "leads.write";
    public const string LeadsAssign = "leads.assign";
    public const string LeadsTransition = "leads.transition";
    public const string LeadsImport = "leads.import";
    public const string LeadsExport = "leads.export";
    public const string LeadsDelete = "leads.delete";

    public const string SalesRead = "sales.read";
    public const string SalesRecord = "sales.record";
    public const string SalesValidate = "sales.validate";
    public const string SalesFund = "sales.fund";
    public const string SalesWrite = "sales.write";

    public const string CallbacksRead = "callbacks.read";
    public const string CallbacksWrite = "callbacks.write";

    // Workspace
    public const string DashboardView = "dashboard.view";
    public const string AgentPanelUse = "agent.use";
    public const string QueueRead = "queue.read";
    public const string QueueWrite = "queue.write";

    // Operations
    public const string SupervisorView = "supervisor.view";
    public const string SupervisorControl = "supervisor.control";
    public const string CallCenterView = "callcenter.view";
    public const string CallCenterControl = "callcenter.control";
    public const string QaView = "qa.view";
    public const string QaSubmit = "qa.submit";
    public const string QaWrite = "qa.write";

    // Finance
    public const string CommissionsView = "commissions.view";
    public const string CommissionsWrite = "commissions.write";
    public const string PayrollView = "payroll.view";
    public const string PayrollProcess = "payroll.process";

    // Resources
    public const string KnowledgeView = "knowledge.view";
    public const string KnowledgeWrite = "knowledge.write";
    public const string ChatRead = "chat.read";
    public const string ChatWrite = "chat.write";

    // Insights
    public const string ReportsView = "reports.view";

    // Administration
    public const string WorkflowsView = "workflows.view";
    public const string WorkflowsManage = "workflows.manage";
    public const string CampaignsView = "campaigns.view";
    public const string CampaignsManage = "campaigns.manage";
    public const string ScriptsView = "scripts.view";
    public const string ScriptsManage = "scripts.manage";
    public const string DncView = "dnc.view";
    public const string DncManage = "dnc.manage";

    public const string UsersRead = "users.read";
    public const string UsersManage = "users.manage";
    public const string TeamRead = "team.read";
    public const string TeamWrite = "team.write";

    public const string RolesRead = "roles.read";
    public const string RolesManage = "roles.manage";
    public const string PermissionsManage = "permissions.manage";

    public const string AgenciesView = "agencies.view";
    public const string AgenciesCreate = "agencies.create";
    public const string AgenciesManage = "agencies.manage";
    public const string IpAllowlistManage = "ip-allowlist.manage";

    public const string CallCentersView = "callcenters.view";
    public const string CallCentersManage = "callcenters.manage";

    public const string IntegrationsView = "integrations.view";
    public const string IntegrationsManage = "integrations.manage";

    public static readonly string[] All = typeof(Permissions).GetFields()
        .Where(f => f.IsLiteral && !f.IsInitOnly)
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToArray();
}

public interface IPermissionService
{
    Task<bool> HasAsync(Guid userId, string permission, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetForUserAsync(Guid userId, CancellationToken ct = default);
    Task GrantToRoleAsync(string roleName, IEnumerable<string> permissionCodes, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetForRoleAsync(Guid roleId, CancellationToken ct = default);
    Task SetForRoleAsync(Guid roleId, IEnumerable<string> permissionCodes, CancellationToken ct = default);
}
