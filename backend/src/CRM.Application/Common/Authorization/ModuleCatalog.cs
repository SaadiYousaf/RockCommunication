namespace CRM.Application.Common.Authorization;

/// <summary>
/// Canonical list of frontend modules. Codes match the keys checked by the React sidebar
/// and route guards. Adding a module here and re-seeding makes it available for admins
/// to assign to roles.
/// </summary>
public static class Modules
{
    public sealed record Definition(string Code, string Name, string Group, string RoutePath, string Icon, int SortOrder);

    public const string Dashboard = "dashboard";
    public const string Team = "team";
    public const string AgentPanel = "agent";
    public const string MyQueue = "queue";
    public const string Leads = "leads";
    public const string LeadsSearch = "leads.search";
    public const string Sales = "sales";
    public const string Callbacks = "callbacks";
    public const string Supervisor = "supervisor";
    public const string CallCenter = "callcenter";
    public const string Qa = "qa";
    public const string Commissions = "commissions";
    public const string Payroll = "payroll";
    public const string Knowledge = "knowledge";
    public const string Documents = "documents";
    public const string Chat = "chat";
    public const string Reports = "reports";
    public const string Workflows = "workflows";
    public const string Campaigns = "campaigns";
    public const string Scripts = "scripts";
    public const string Dnc = "dnc";
    public const string UsersManagement = "users.manage";
    public const string RolesManagement = "roles.manage";
    public const string Admin = "admin";

    public static readonly Definition[] All =
    {
        new(Dashboard,         "Dashboard",          "Workspace",      "/dashboard",      "dashboard", 10),
        new(Team,              "Team",               "Workspace",      "/team",           "users",     15),
        new(AgentPanel,        "Agent Panel",        "Workspace",      "/agent",          "phone",     20),
        new(MyQueue,           "My Queue",           "Workspace",      "/queue",          "inbox",     30),
        new(Leads,             "Leads",              "Pipeline",       "/leads",          "list",      40),
        new(LeadsSearch,       "Search & Dedup",     "Pipeline",       "/leads/search",   "search",    50),
        new(Sales,             "Sales",              "Pipeline",       "/sales",          "briefcase", 60),
        new(Callbacks,         "Callbacks",          "Pipeline",       "/callbacks",      "clock",     70),
        new(Supervisor,        "Supervisor",         "Operations",     "/supervisor",     "shield",    80),
        new(CallCenter,        "Call Center",        "Operations",     "/callcenter",     "phone",     90),
        new(Qa,                "Quality Assurance",  "Operations",     "/qa",             "check",    100),
        new(Commissions,       "Commissions",        "Finance",        "/commissions",    "dollar",   110),
        new(Payroll,           "Payroll",            "Finance",        "/payroll",        "dollar",   120),
        new(Knowledge,         "Knowledge Base",     "Resources",      "/knowledge",      "book",     130),
        new(Documents,         "Documents",          "Resources",      "/documents",      "doc",      135),
        new(Chat,              "Chat",               "Resources",      "/chat",           "message",  140),
        new(Reports,           "Reports",            "Insights",       "/reports",        "chart",    150),
        new(Workflows,         "Workflows",          "Administration", "/workflows",      "flow",     160),
        new(Campaigns,         "Campaigns",          "Administration", "/campaigns",      "target",   170),
        new(Scripts,           "Scripts",            "Administration", "/scripts",        "doc",      180),
        new(Dnc,               "DNC List",           "Administration", "/dnc",            "ban",      190),
        new(UsersManagement,   "User Management",    "Administration", "/admin/users",    "users",    200),
        new(RolesManagement,   "Role Management",    "Administration", "/admin/roles",    "shield",   210),
        new(Admin,             "Admin",              "Administration", "/admin",          "building", 220)
    };
}
