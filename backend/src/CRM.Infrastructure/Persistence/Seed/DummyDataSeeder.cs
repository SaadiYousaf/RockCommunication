using CRM.Domain.Entities;
using CRM.Domain.Enums;
using CRM.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CRM.Infrastructure.Persistence.Seed;

internal static class DummyDataSeeder
{
    public static async Task SeedAsync(
        AppDbContext db,
        UserManager<ApplicationUser> users,
        RoleManager<ApplicationRole> roles,
        Agency agency)
    {
        // Idempotency: skip if we've already seeded a meaningful number of leads
        if (await db.Leads.CountAsync() >= 50) return;

        var rnd = new Random(42);

        // ---- Teams ----
        var teamNames = new[] { "Falcons", "Sharks", "Eagles" };
        var teams = new List<Team>();
        foreach (var name in teamNames)
        {
            var team = await db.Teams.FirstOrDefaultAsync(t => t.AgencyId == agency.Id && t.Name == name);
            if (team is null)
            {
                team = new Team { AgencyId = agency.Id, Name = name, Vertical = "ACA" };
                db.Teams.Add(team);
            }
            teams.Add(team);
        }
        await db.SaveChangesAsync();

        // ---- Users (one per role + multiple closers/fronters) ----
        var seedUsers = new (string user, string display, string email, string role, string? team)[]
        {
            ("pm.olivia",     "Olivia Carter",   "olivia.carter@crm.local",   Roles.ProgramManager, null),
            ("lead.marcus",   "Marcus Reyes",    "marcus.reyes@crm.local",    Roles.TeamLead,       "Falcons"),
            ("lead.priya",    "Priya Sharma",    "priya.sharma@crm.local",    Roles.TeamLead,       "Sharks"),
            ("front.james",   "James Kowalski",  "james.kowalski@crm.local",  Roles.Fronter,        "Falcons"),
            ("front.aisha",   "Aisha Patel",     "aisha.patel@crm.local",     Roles.Fronter,        "Sharks"),
            ("front.daniel",  "Daniel Romero",   "daniel.romero@crm.local",   Roles.Fronter,        "Eagles"),
            ("verify.sara",   "Sara Lin",        "sara.lin@crm.local",        Roles.Verifier,       "Falcons"),
            ("jr.tom",        "Tom Bennett",     "tom.bennett@crm.local",     Roles.JrCloser,       "Falcons"),
            ("jr.maria",      "Maria Santos",    "maria.santos@crm.local",    Roles.JrCloser,       "Sharks"),
            ("close.ethan",   "Ethan Walker",    "ethan.walker@crm.local",    Roles.Closer,         "Falcons"),
            ("close.zoe",     "Zoe Mitchell",    "zoe.mitchell@crm.local",    Roles.Closer,         "Sharks"),
            ("close.amir",    "Amir Hassan",     "amir.hassan@crm.local",     Roles.Closer,         "Eagles"),
            ("validate.nina", "Nina Brooks",     "nina.brooks@crm.local",     Roles.Validator,      null),
            ("followup.luis", "Luis Diaz",       "luis.diaz@crm.local",       Roles.Followups,      "Eagles"),
            ("winback.kate",  "Kate O'Brien",    "kate.obrien@crm.local",     Roles.Winbacks,       "Eagles"),
        };

        var createdUsers = new List<ApplicationUser>();
        foreach (var (uname, display, email, role, teamName) in seedUsers)
        {
            var existing = await users.FindByNameAsync(uname);
            if (existing is null)
            {
                var u = new ApplicationUser
                {
                    UserName = uname,
                    Email = email,
                    EmailConfirmed = true,
                    AgencyId = agency.Id,
                    DisplayName = display,
                    IsActive = true,
                    TeamId = teamName is null ? null : teams.FirstOrDefault(t => t.Name == teamName)?.Id,
                };
                var result = await users.CreateAsync(u, "Demo@123!");
                if (result.Succeeded)
                {
                    await users.AddToRoleAsync(u, role);
                    existing = u;
                }
            }
            if (existing is not null) createdUsers.Add(existing);
        }

        // Set team leads
        foreach (var team in teams)
        {
            if (team.TeamLeadUserId is null)
            {
                var lead = createdUsers.FirstOrDefault(u => u.TeamId == team.Id &&
                    users.IsInRoleAsync(u, Roles.TeamLead).GetAwaiter().GetResult());
                if (lead is not null) team.TeamLeadUserId = lead.Id;
            }
        }
        await db.SaveChangesAsync();

        var fronters = createdUsers.Where(u => users.IsInRoleAsync(u, Roles.Fronter).GetAwaiter().GetResult()).ToList();
        var closers  = createdUsers.Where(u => users.IsInRoleAsync(u, Roles.Closer).GetAwaiter().GetResult()).ToList();
        var jrClosers = createdUsers.Where(u => users.IsInRoleAsync(u, Roles.JrCloser).GetAwaiter().GetResult()).ToList();
        var validators = createdUsers.Where(u => users.IsInRoleAsync(u, Roles.Validator).GetAwaiter().GetResult()).ToList();

        // ---- Verticals ----
        if (!await db.Verticals.AnyAsync(v => v.AgencyId == agency.Id))
        {
            db.Verticals.AddRange(
                new Vertical { AgencyId = agency.Id, Name = "ACA Health",    Description = "Affordable Care Act health plans", IsActive = true },
                new Vertical { AgencyId = agency.Id, Name = "Medicare",      Description = "Medicare supplemental plans",      IsActive = true },
                new Vertical { AgencyId = agency.Id, Name = "Final Expense", Description = "Final expense / burial insurance", IsActive = true });
            await db.SaveChangesAsync();
        }

        // ---- Horizontals ----
        if (!await db.Horizontals.AnyAsync(h => h.AgencyId == agency.Id))
        {
            db.Horizontals.AddRange(
                new Horizontal { AgencyId = agency.Id, Name = "East Region",  Description = "Eastern US territory",            IsActive = true },
                new Horizontal { AgencyId = agency.Id, Name = "West Region",  Description = "Western US territory",            IsActive = true },
                new Horizontal { AgencyId = agency.Id, Name = "Retention",    Description = "Cross-vertical retention desk",   IsActive = true });
            await db.SaveChangesAsync();
        }

        // ---- Campaigns ----
        if (!await db.Campaigns.AnyAsync(c => c.AgencyId == agency.Id))
        {
            db.Campaigns.AddRange(
                new Campaign { AgencyId = agency.Id, Code = "OEP-Q1",   Name = "OEP Q1 Push",  IsActive = true },
                new Campaign { AgencyId = agency.Id, Code = "MED-AEP",  Name = "Medicare AEP", IsActive = true },
                new Campaign { AgencyId = agency.Id, Code = "WB-MAY",   Name = "Winback May",  IsActive = true });
            await db.SaveChangesAsync();
        }

        // ---- Lead sources ----
        if (!await db.LeadSources.AnyAsync(s => s.AgencyId == agency.Id))
        {
            db.LeadSources.AddRange(
                new LeadSource { AgencyId = agency.Id, Code = "FB", Name = "Facebook Ads",  IsActive = true },
                new LeadSource { AgencyId = agency.Id, Code = "GS", Name = "Google Search", IsActive = true },
                new LeadSource { AgencyId = agency.Id, Code = "RF", Name = "Referral",      IsActive = true },
                new LeadSource { AgencyId = agency.Id, Code = "IB", Name = "Inbound Call",  IsActive = true });
            await db.SaveChangesAsync();
        }

        // ---- Leads ----
        var firstNames = new[] { "Liam","Emma","Noah","Olivia","Mason","Ava","Lucas","Sophia","Ethan","Mia","Aiden","Isabella","Jackson","Charlotte","Logan","Amelia","Owen","Harper","Caleb","Evelyn","Wyatt","Abigail","Henry","Emily","Sebastian","Elizabeth","Carter","Sofia","Jayden","Avery" };
        var lastNames  = new[] { "Smith","Johnson","Williams","Brown","Jones","Garcia","Miller","Davis","Rodriguez","Martinez","Hernandez","Lopez","Gonzalez","Wilson","Anderson","Thomas","Taylor","Moore","Jackson","Martin","Lee","Perez","Thompson","White","Harris","Sanchez","Clark","Ramirez","Lewis","Robinson" };
        var states     = new[] { "TX","FL","CA","NY","GA","NC","OH","IL","PA","AZ" };
        var cities     = new[] { "Austin","Miami","Los Angeles","Brooklyn","Atlanta","Charlotte","Columbus","Chicago","Philadelphia","Phoenix" };
        var sources    = new[] { "Facebook Ads","Google Search","Referral","Inbound Call" };
        var verticalsList = await db.Verticals.Where(v => v.AgencyId == agency.Id).ToListAsync();
        var campaignsList = await db.Campaigns.Where(c => c.AgencyId == agency.Id).ToListAsync();

        var stages = new[]
        {
            (WorkflowStage.New, 18),
            (WorkflowStage.Fronted, 12),
            (WorkflowStage.Verified, 9),
            (WorkflowStage.JrClosed, 7),
            (WorkflowStage.Closed, 8),
            (WorkflowStage.Validated, 5),
            (WorkflowStage.Funded, 6),
            (WorkflowStage.Followup, 4),
            (WorkflowStage.Lost, 6),
        };

        var leadList = new List<Lead>();
        foreach (var (stage, count) in stages)
        {
            for (int i = 0; i < count; i++)
            {
                var fn = firstNames[rnd.Next(firstNames.Length)];
                var ln = lastNames[rnd.Next(lastNames.Length)];
                var stateIdx = rnd.Next(states.Length);
                var assigned = (stage == WorkflowStage.New ? null
                    : stage is WorkflowStage.Closed or WorkflowStage.Validated or WorkflowStage.Funded
                        ? closers[rnd.Next(closers.Count)]
                    : stage == WorkflowStage.JrClosed ? jrClosers[rnd.Next(jrClosers.Count)]
                    : stage is WorkflowStage.Fronted or WorkflowStage.Verified
                        ? fronters[rnd.Next(fronters.Count)]
                    : fronters[rnd.Next(fronters.Count)])?.Id;

                var disposition = stage switch
                {
                    WorkflowStage.Closed or WorkflowStage.Validated or WorkflowStage.Funded => LeadDisposition.Sold,
                    WorkflowStage.Lost => LeadDisposition.NotInterested,
                    WorkflowStage.Followup => LeadDisposition.CallBack,
                    _ => LeadDisposition.Interested,
                };

                var createdAt = DateTime.UtcNow.AddDays(-rnd.Next(0, 60)).AddHours(-rnd.Next(0, 24));

                var lead = new Lead
                {
                    AgencyId = agency.Id,
                    FirstName = fn,
                    LastName = ln,
                    Email = $"{fn.ToLower()}.{ln.ToLower()}@example.com",
                    PhoneNumber = $"+1{rnd.Next(200, 999)}{rnd.Next(200, 999):D3}{rnd.Next(0, 9999):D4}",
                    City = cities[stateIdx],
                    State = states[stateIdx],
                    PostalCode = $"{rnd.Next(10000, 99999)}",
                    DateOfBirth = DateTime.UtcNow.AddYears(-rnd.Next(28, 70)).AddDays(-rnd.Next(0, 365)),
                    Stage = stage,
                    Disposition = disposition,
                    Source = sources[rnd.Next(sources.Length)],
                    AssignedUserId = assigned,
                    VerticalId = verticalsList[rnd.Next(verticalsList.Count)].Id,
                    CampaignId = campaignsList[rnd.Next(campaignsList.Count)].Id,
                    ConsentCaptured = rnd.NextDouble() > 0.2,
                    JornayaVerified = rnd.NextDouble() > 0.4,
                    Score = rnd.Next(20, 95),
                    Notes = i % 4 == 0 ? "Hot lead — interested in family plan." : null,
                    CreatedAt = createdAt,
                };
                leadList.Add(lead);
            }
        }
        db.Leads.AddRange(leadList);
        await db.SaveChangesAsync();

        // ---- Lead activities (transitions) ----
        var activityList = new List<LeadActivity>();
        foreach (var lead in leadList)
        {
            var path = StagePath(lead.Stage);
            var when = lead.CreatedAt;
            for (int i = 1; i < path.Length; i++)
            {
                when = when.AddHours(rnd.Next(1, 36));
                activityList.Add(new LeadActivity
                {
                    AgencyId = agency.Id,
                    LeadId = lead.Id,
                    UserId = lead.AssignedUserId ?? createdUsers[rnd.Next(createdUsers.Count)].Id,
                    FromStage = path[i - 1],
                    ToStage = path[i],
                    Disposition = i == path.Length - 1 ? lead.Disposition : LeadDisposition.Interested,
                    Notes = i == path.Length - 1 ? "Stage transition" : null,
                    OccurredAt = when,
                });
            }
        }
        db.LeadActivities.AddRange(activityList);
        await db.SaveChangesAsync();

        // ---- Sales for closed/validated/funded leads ----
        var carriers = new[] { "Aetna", "UnitedHealth", "Cigna", "Anthem", "Humana" };
        var saleList = new List<Sale>();
        foreach (var lead in leadList.Where(l => l.Stage is WorkflowStage.Closed or WorkflowStage.Validated or WorkflowStage.Funded))
        {
            var closer = closers[rnd.Next(closers.Count)];
            var validator = lead.Stage != WorkflowStage.Closed ? validators[rnd.Next(validators.Count)] : null;
            var monthly = (decimal)Math.Round(rnd.NextDouble() * 250 + 80, 2);
            var soldAt = lead.CreatedAt.AddDays(rnd.Next(1, 7));
            var sale = new Sale
            {
                AgencyId = agency.Id,
                LeadId = lead.Id,
                CloserUserId = closer.Id,
                ValidatorUserId = validator?.Id,
                Carrier = carriers[rnd.Next(carriers.Length)],
                PolicyNumber = $"POL-{rnd.Next(100000, 999999)}",
                MonthlyPremium = monthly,
                AnnualPremium = monthly * 12,
                SoldAt = soldAt,
                ValidatedAt = lead.Stage != WorkflowStage.Closed ? soldAt.AddDays(1) : null,
                FundedAt    = lead.Stage == WorkflowStage.Funded ? soldAt.AddDays(2) : null,
                IsInternalSale = false,
            };
            saleList.Add(sale);
        }
        db.Sales.AddRange(saleList);
        await db.SaveChangesAsync();

        // ---- Commission entries for funded sales ----
        var commissionList = new List<CommissionEntry>();
        foreach (var sale in saleList.Where(s => s.FundedAt is not null))
        {
            commissionList.Add(new CommissionEntry
            {
                AgencyId = agency.Id,
                SaleId = sale.Id,
                AgentUserId = sale.CloserUserId,
                RuleName = "CloserFlatRate",
                Amount = Math.Round(sale.MonthlyPremium * 1.2m, 2),
                EarnedAt = sale.FundedAt!.Value,
                Paid = false,
                Note = $"Closer commission for {sale.Carrier} policy",
            });
        }
        db.CommissionEntries.AddRange(commissionList);
        await db.SaveChangesAsync();

        // ---- Scheduled callbacks ----
        var callbackList = new List<ScheduledCallback>();
        foreach (var lead in leadList.Where(l => l.Stage is WorkflowStage.Followup or WorkflowStage.Fronted or WorkflowStage.Verified).Take(15))
        {
            callbackList.Add(new ScheduledCallback
            {
                AgencyId = agency.Id,
                LeadId = lead.Id,
                AssignedUserId = lead.AssignedUserId ?? fronters[rnd.Next(fronters.Count)].Id,
                ScheduledFor = DateTime.UtcNow.AddHours(rnd.Next(-12, 96)),
                Reason = "Customer requested follow-up",
                Completed = false,
            });
        }
        db.ScheduledCallbacks.AddRange(callbackList);
        await db.SaveChangesAsync();
    }

    private static WorkflowStage[] StagePath(WorkflowStage stage) => stage switch
    {
        WorkflowStage.New      => new[] { WorkflowStage.New },
        WorkflowStage.Fronted  => new[] { WorkflowStage.New, WorkflowStage.Fronted },
        WorkflowStage.Verified => new[] { WorkflowStage.New, WorkflowStage.Fronted, WorkflowStage.Verified },
        WorkflowStage.JrClosed => new[] { WorkflowStage.New, WorkflowStage.Fronted, WorkflowStage.Verified, WorkflowStage.JrClosed },
        WorkflowStage.Closed   => new[] { WorkflowStage.New, WorkflowStage.Fronted, WorkflowStage.Verified, WorkflowStage.Closed },
        WorkflowStage.Validated=> new[] { WorkflowStage.New, WorkflowStage.Fronted, WorkflowStage.Verified, WorkflowStage.Closed, WorkflowStage.Validated },
        WorkflowStage.Funded   => new[] { WorkflowStage.New, WorkflowStage.Fronted, WorkflowStage.Verified, WorkflowStage.Closed, WorkflowStage.Validated, WorkflowStage.Funded },
        WorkflowStage.Followup => new[] { WorkflowStage.New, WorkflowStage.Fronted, WorkflowStage.Followup },
        WorkflowStage.Winback  => new[] { WorkflowStage.New, WorkflowStage.Lost, WorkflowStage.Winback },
        WorkflowStage.Lost     => new[] { WorkflowStage.New, WorkflowStage.Fronted, WorkflowStage.Lost },
        _ => new[] { WorkflowStage.New },
    };
}
