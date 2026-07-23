using CRM.Application.Common.Authorization;
using CRM.Domain.Entities;
using Modules = CRM.Application.Common.Authorization.Modules;
using CRM.Domain.Enums;
using CRM.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CRM.Infrastructure.Persistence.Seed;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var roles = sp.GetRequiredService<RoleManager<ApplicationRole>>();
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var config = sp.GetRequiredService<IConfiguration>();

        var providerName = db.Database.ProviderName ?? string.Empty;
        var isSqlite = providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);

        if (isSqlite)
            await db.Database.MigrateAsync();
        else
            await db.Database.EnsureCreatedAsync();

        foreach (var role in Roles.All)
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new ApplicationRole(role));

        var agency = await db.Agencies.FirstOrDefaultAsync(a => a.Name == "Default Agency");
        if (agency is null)
        {
            agency = new Agency { Name = "Default Agency", Code = "DEFAULT" };
            db.Agencies.Add(agency);
            await db.SaveChangesAsync();
        }

        var admin = await users.FindByNameAsync("admin");
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                UserName = "admin",
                Email = "admin@crm.local",
                EmailConfirmed = true,
                AgencyId = agency.Id,
                DisplayName = "System Admin",
                IsActive = true
            };
            var result = await users.CreateAsync(admin, "Admin@123!");
            if (result.Succeeded)
                await users.AddToRoleAsync(admin, Roles.Admin);
        }

        // Global SuperAdmin (no agency). Convention: AgencyId = Guid.Empty → cross-tenant.
        var superAdmin = await users.FindByNameAsync("superadmin");
        if (superAdmin is null)
        {
            superAdmin = new ApplicationUser
            {
                UserName = "superadmin",
                Email = "superadmin@crm.local",
                EmailConfirmed = true,
                AgencyId = Guid.Empty,
                DisplayName = "Super Admin",
                IsActive = true
            };
            var pw = config["Seed:SuperAdminPassword"] ?? "SuperAdmin@123!";
            var result = await users.CreateAsync(superAdmin, pw);
            if (result.Succeeded)
                await users.AddToRoleAsync(superAdmin, Roles.SuperAdmin);
        }

        // Recovery-account guarantee: the built-in admin + superadmin must never be
        // lockable or deactivated. Otherwise a burst of failed sign-ins (Identity locks
        // after N attempts) or an accidental toggle can permanently wall off the only
        // accounts that can fix everything else. Re-assert on every startup so any such
        // state self-heals on the next deploy/restart.
        foreach (var privileged in new[] { admin, superAdmin })
        {
            if (privileged is null) continue;
            var changed = false;
            if (privileged.LockoutEnabled) { privileged.LockoutEnabled = false; changed = true; }
            if (!privileged.IsActive) { privileged.IsActive = true; changed = true; }
            if (privileged.LockoutEnd is not null) { privileged.LockoutEnd = null; changed = true; }
            if (privileged.AccessFailedCount != 0) { privileged.AccessFailedCount = 0; changed = true; }
            if (changed) await users.UpdateAsync(privileged);
        }

        await SeedPermissionsAsync(db, roles);
        await SeedModulesAsync(db, roles);

        var seedDummy = config.GetValue("Seed:DummyData", false);
        if (seedDummy)
        {
            await DummyDataSeeder.SeedAsync(db, users, roles, agency);
            await FeatureSeeder.SeedAsync(db, users, roles, agency);
        }

        // Ensure every agency has a default call center and stamp any pre-existing pipeline
        // rows (including freshly-seeded dummy data) onto it. Additive + idempotent.
        await CallCenterBackfill.RunAsync(db);
    }

    private static async Task SeedPermissionsAsync(AppDbContext db, RoleManager<ApplicationRole> roles)
    {
        foreach (var code in Permissions.All)
        {
            if (!await db.Permissions.AnyAsync(p => p.Code == code))
                db.Permissions.Add(new Permission
                {
                    Code = code,
                    Group = code.Split('.')[0],
                    Description = code
                });
        }
        await db.SaveChangesAsync();

        // CEO defaults: full agency-scoped management. Excludes super-admin-only items.
        string[] ceoGrants = Permissions.All
            .Where(p => p != Permissions.AgenciesCreate)
            .ToArray();

        // Common read bundles
        string[] agentReads = new[]
        {
            Permissions.DashboardView, Permissions.AgentPanelUse, Permissions.QueueRead,
            Permissions.LeadsRead, Permissions.CallbacksRead, Permissions.KnowledgeView,
            Permissions.ChatRead
        };
        string[] agentWrites = new[]
        {
            Permissions.LeadsTransition, Permissions.CallbacksWrite,
            Permissions.QueueWrite, Permissions.ChatWrite
        };
        string[] closerExtras = new[]
        {
            Permissions.SalesRead, Permissions.SalesRecord, Permissions.CommissionsView
        };

        string[] teamLeadGrants = agentReads.Concat(agentWrites).Concat(new[]
        {
            Permissions.LeadsWrite, Permissions.LeadsAssign,
            Permissions.SupervisorView, Permissions.SupervisorControl,
            Permissions.CallCenterView,
            Permissions.QaView, Permissions.QaSubmit, Permissions.QaWrite,
            Permissions.SalesRead,
            Permissions.UsersRead, Permissions.TeamRead,
            Permissions.ReportsView
        }).Distinct().ToArray();

        string[] qaManagerGrants = new[]
        {
            Permissions.DashboardView, Permissions.LeadsRead, Permissions.SupervisorView,
            Permissions.QaView, Permissions.QaSubmit, Permissions.QaWrite,
            Permissions.ReportsView, Permissions.KnowledgeView, Permissions.ChatRead, Permissions.ChatWrite
        };
        string[] projectManagerGrants = teamLeadGrants.Concat(new[]
        {
            Permissions.CampaignsView, Permissions.CampaignsManage,
            Permissions.WorkflowsView, Permissions.WorkflowsManage,
            Permissions.ScriptsView, Permissions.ScriptsManage,
            Permissions.UsersManage, Permissions.TeamWrite
        }).Distinct().ToArray();
        string[] techLeadGrants = new[]
        {
            Permissions.DashboardView, Permissions.LeadsRead, Permissions.SupervisorView,
            Permissions.WorkflowsView, Permissions.WorkflowsManage,
            Permissions.ScriptsView, Permissions.ScriptsManage,
            Permissions.CampaignsView, Permissions.CampaignsManage,
            Permissions.IntegrationsView, Permissions.IntegrationsManage
        };

        var roleGrants = new Dictionary<string, string[]>
        {
            // SuperAdmin is enforced via the override in PermissionHandler — no grants needed,
            // but seed all permissions defensively in case the override is ever removed.
            [Roles.SuperAdmin] = Permissions.All,
            [Roles.Admin] = Permissions.All.Where(p => p != Permissions.AgenciesCreate).ToArray(),
            [Roles.CEO] = ceoGrants,
            [Roles.QAManager] = qaManagerGrants,
            [Roles.ProjectManager] = projectManagerGrants,
            [Roles.TechLead] = techLeadGrants,
            [Roles.ProgramManager] = Permissions.All.Where(p => p != Permissions.AgenciesCreate).ToArray(),
            [Roles.TeamLead] = teamLeadGrants,
            // Every agent-level role gets ChatWrite — internal chat is how the floor
            // communicates; read-only chat is useless. Same for QueueWrite (mark
            // queue items handled).
            [Roles.Fronter] = agentReads.Concat(new[] { Permissions.LeadsTransition, Permissions.CallbacksWrite, Permissions.ChatWrite, Permissions.QueueWrite }).ToArray(),
            [Roles.Verifier] = agentReads.Concat(new[] { Permissions.LeadsTransition, Permissions.SalesRead, Permissions.ChatWrite, Permissions.QueueWrite }).ToArray(),
            [Roles.JrCloser] = agentReads.Concat(agentWrites).Concat(closerExtras).ToArray(),
            [Roles.Closer] = agentReads.Concat(agentWrites).Concat(closerExtras).ToArray(),
            [Roles.Validator] = new[]
            {
                Permissions.DashboardView, Permissions.QueueRead, Permissions.LeadsRead,
                Permissions.SalesRead, Permissions.SalesValidate,
                Permissions.QaView, Permissions.QaSubmit, Permissions.KnowledgeView,
                Permissions.ChatRead, Permissions.ChatWrite
            },
            [Roles.SelfValidator] = new[]
            {
                Permissions.DashboardView, Permissions.QueueRead, Permissions.LeadsRead,
                Permissions.SalesRead, Permissions.SalesRecord, Permissions.SalesValidate,
                Permissions.CommissionsView, Permissions.QaView, Permissions.KnowledgeView,
                Permissions.ChatRead, Permissions.ChatWrite
            },
            [Roles.Followups] = agentReads.Concat(new[] { Permissions.LeadsTransition, Permissions.CallbacksWrite, Permissions.ChatWrite, Permissions.QueueWrite }).ToArray(),
            [Roles.Correspondence] = agentReads.Concat(new[] { Permissions.LeadsTransition, Permissions.CallbacksWrite, Permissions.ChatWrite, Permissions.QueueWrite }).ToArray(),
            [Roles.Winbacks] = agentReads.Concat(new[] { Permissions.LeadsTransition, Permissions.CallbacksWrite, Permissions.ChatWrite, Permissions.QueueWrite }).ToArray()
        };

        foreach (var (roleName, codes) in roleGrants)
        {
            var role = await roles.FindByNameAsync(roleName);
            if (role is null) continue;
            var codeSet = codes.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var permIds = await db.Permissions.Where(p => codeSet.Contains(p.Code)).Select(p => p.Id).ToListAsync();
            foreach (var pid in permIds)
            {
                if (!await db.RolePermissions.AnyAsync(rp => rp.RoleId == role.Id && rp.PermissionId == pid))
                    db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = pid });
            }
        }
        await db.SaveChangesAsync();
    }

    private static async Task SeedModulesAsync(AppDbContext db, RoleManager<ApplicationRole> roles)
    {
        // Upsert canonical module catalog
        foreach (var def in Modules.All)
        {
            var existing = await db.AppModules.FirstOrDefaultAsync(m => m.Code == def.Code);
            if (existing is null)
            {
                db.AppModules.Add(new AppModule
                {
                    Code = def.Code,
                    Name = def.Name,
                    Group = def.Group,
                    RoutePath = def.RoutePath,
                    Icon = def.Icon,
                    SortOrder = def.SortOrder,
                    IsSystem = true,
                    Description = def.Name
                });
            }
            else
            {
                existing.Name = def.Name;
                existing.Group = def.Group;
                existing.RoutePath = def.RoutePath;
                existing.Icon = def.Icon;
                existing.SortOrder = def.SortOrder;
                existing.IsSystem = true;
            }
        }
        await db.SaveChangesAsync();

        // Default module access by role.
        //  Admin/PM           = everything (system + user/role admin)
        //  TeamLead           = floor-supervision: pipeline + ops + reports + chat/KB,
        //                       BUT no user-admin / role-admin / workflow / system admin
        //  Agents (Fronter…)  = the surfaces they actually need to do their job
        string[] all = Modules.All.Select(m => m.Code).ToArray();
        string[] manager = new[] {
            Modules.Dashboard, Modules.Team, Modules.AgentPanel, Modules.MyQueue,
            Modules.Leads, Modules.LeadsSearch,
            Modules.Sales, Modules.Callbacks,
            Modules.Supervisor, Modules.CallCenter, Modules.Qa,
            Modules.Commissions,
            Modules.Knowledge, Modules.Chat, Modules.Reports,
            Modules.Campaigns, Modules.Scripts, Modules.Dnc,
        };
        string[] agent = new[] { Modules.Dashboard, Modules.AgentPanel, Modules.MyQueue, Modules.Leads, Modules.Callbacks,
            Modules.Knowledge, Modules.Chat };
        string[] closer = agent.Concat(new[] { Modules.Sales, Modules.Commissions }).ToArray();
        string[] validator = new[] { Modules.Dashboard, Modules.MyQueue, Modules.Leads, Modules.Sales, Modules.Qa, Modules.Knowledge, Modules.Chat };

        var roleModuleGrants = new Dictionary<string, string[]>
        {
            [Domain.Enums.Roles.SuperAdmin] = all,
            [Domain.Enums.Roles.Admin] = all,
            [Domain.Enums.Roles.CEO] = all,
            [Domain.Enums.Roles.ProjectManager] = manager,
            [Domain.Enums.Roles.QAManager] = new[] { Modules.Dashboard, Modules.Qa, Modules.Supervisor, Modules.Reports, Modules.Knowledge, Modules.Chat },
            [Domain.Enums.Roles.TechLead] = manager,
            [Domain.Enums.Roles.ProgramManager] = all,
            [Domain.Enums.Roles.TeamLead] = manager,
            [Domain.Enums.Roles.Fronter] = agent,
            [Domain.Enums.Roles.Verifier] = agent,
            [Domain.Enums.Roles.JrCloser] = closer,
            [Domain.Enums.Roles.Closer] = closer,
            [Domain.Enums.Roles.Validator] = validator,
            [Domain.Enums.Roles.SelfValidator] = validator.Concat(new[] { Modules.Commissions }).ToArray(),
            [Domain.Enums.Roles.Followups] = agent,
            [Domain.Enums.Roles.Correspondence] = agent,
            [Domain.Enums.Roles.Winbacks] = agent
        };

        // Reconcile (add missing AND remove grants that aren't in the spec) so role
        // changes here propagate to existing rows on next boot.
        var allModules = await db.AppModules.ToListAsync();
        foreach (var (roleName, codes) in roleModuleGrants)
        {
            var role = await roles.FindByNameAsync(roleName);
            if (role is null) continue;

            var codeSet = new HashSet<string>(codes, StringComparer.OrdinalIgnoreCase);
            var desiredModuleIds = allModules.Where(m => codeSet.Contains(m.Code)).Select(m => m.Id).ToHashSet();

            var existing = await db.RoleModules.Where(rm => rm.RoleId == role.Id).ToListAsync();
            // remove what's no longer desired
            var toRemove = existing.Where(rm => !desiredModuleIds.Contains(rm.ModuleId)).ToList();
            if (toRemove.Count > 0) db.RoleModules.RemoveRange(toRemove);
            // add what's missing
            var existingIds = existing.Select(rm => rm.ModuleId).ToHashSet();
            foreach (var mid in desiredModuleIds.Except(existingIds))
                db.RoleModules.Add(new RoleModule { RoleId = role.Id, ModuleId = mid });
        }
        await db.SaveChangesAsync();
    }
}

internal static class ServiceProviderExt
{
    public static T GetRequiredService<T>(this IServiceProvider sp) where T : notnull
        => Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<T>(sp);
}
