using CRM.Domain.Entities;
using CRM.Domain.Enums;
using CRM.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CRM.Infrastructure.Persistence.Seed;

/// <summary>
/// Seeds dummy data for every CRM feature surface so the testing environment is
/// usable end-to-end (Skills, Wrap-up codes, DNC, Scripts, Cadences, Lead lists,
/// QA, Knowledge base, Workflows, Inbound queues, Voicemail, Public endpoints,
/// Chat, and Agent floor activity / call history).
/// </summary>
internal static class FeatureSeeder
{
    public static async Task SeedAsync(
        AppDbContext db,
        UserManager<ApplicationUser> users,
        RoleManager<ApplicationRole> roles,
        Agency agency)
    {
        var rnd = new Random(123);
        var allUsers = await db.Users.Where(u => u.AgencyId == agency.Id).ToListAsync();
        if (allUsers.Count == 0) return;

        var fronters  = await UsersInRoleAsync(db, allUsers, Roles.Fronter);
        var closers   = await UsersInRoleAsync(db, allUsers, Roles.Closer);
        var validators = await UsersInRoleAsync(db, allUsers, Roles.Validator);
        var reviewers = await UsersInRoleAsync(db, allUsers, Roles.TeamLead);
        if (reviewers.Count == 0) reviewers = closers;

        var allCallers = fronters.Concat(closers).ToList();
        if (allCallers.Count == 0) allCallers = allUsers;

        await SeedSkillsAsync(db, agency, allUsers, rnd);
        await SeedWrapUpCodesAsync(db, agency);
        await SeedDncAsync(db, agency, rnd);
        await SeedScriptsAsync(db, agency);
        await SeedKnowledgeAsync(db, agency, allUsers);
        await SeedQaAsync(db, agency, allUsers, reviewers, rnd);
        await SeedCadencesAsync(db, agency);
        await SeedLeadListsAsync(db, agency, allUsers, rnd);
        await SeedWorkflowsAsync(db, agency, rnd);
        await SeedInboundQueuesAsync(db, agency, allUsers);
        await SeedChatAsync(db, agency, allUsers, rnd);
        await SeedCallHistoryAsync(db, agency, allCallers, rnd);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static async Task<List<ApplicationUser>> UsersInRoleAsync(AppDbContext db, List<ApplicationUser> allUsers, string role)
    {
        var roleEntity = await db.Roles.FirstOrDefaultAsync(r => r.Name == role);
        if (roleEntity is null) return new();
        var userIds = await db.UserRoles.Where(ur => ur.RoleId == roleEntity.Id).Select(ur => ur.UserId).ToListAsync();
        return allUsers.Where(u => userIds.Contains(u.Id)).ToList();
    }

    // -------------------------------------------------------------------------
    // Skills + agent assignments
    // -------------------------------------------------------------------------

    private static async Task SeedSkillsAsync(AppDbContext db, Agency agency, List<ApplicationUser> users, Random rnd)
    {
        if (await db.Skills.AnyAsync(s => s.AgencyId == agency.Id)) return;

        var skills = new[]
        {
            new Skill { AgencyId = agency.Id, Code = "EN", Name = "English",  IsActive = true },
            new Skill { AgencyId = agency.Id, Code = "ES", Name = "Spanish",  IsActive = true },
            new Skill { AgencyId = agency.Id, Code = "MED", Name = "Medicare expert",  IsActive = true },
            new Skill { AgencyId = agency.Id, Code = "ACA", Name = "ACA expert",       IsActive = true },
            new Skill { AgencyId = agency.Id, Code = "FE",  Name = "Final Expense",    IsActive = true },
        };
        db.Skills.AddRange(skills);
        await db.SaveChangesAsync();

        // Each agent gets EN by default + 1-2 specialty skills
        var agentSkills = new List<AgentSkill>();
        var en = skills[0];
        foreach (var u in users)
        {
            agentSkills.Add(new AgentSkill { AgencyId = agency.Id, UserId = u.Id, SkillId = en.Id, Proficiency = 5 });
            var picks = skills.Skip(1).OrderBy(_ => rnd.Next()).Take(rnd.Next(1, 3)).ToList();
            foreach (var p in picks)
                agentSkills.Add(new AgentSkill { AgencyId = agency.Id, UserId = u.Id, SkillId = p.Id, Proficiency = rnd.Next(2, 6) });
        }
        db.AgentSkills.AddRange(agentSkills);
        await db.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // Wrap-up codes
    // -------------------------------------------------------------------------

    private static async Task SeedWrapUpCodesAsync(AppDbContext db, Agency agency)
    {
        if (await db.WrapUpCodes.AnyAsync(w => w.AgencyId == agency.Id)) return;
        db.WrapUpCodes.AddRange(
            new WrapUpCode { AgencyId = agency.Id, Code = "SALE",      Label = "Closed sale",       IsSale = true,  IsContact = true,  IsRetry = false, IsActive = true },
            new WrapUpCode { AgencyId = agency.Id, Code = "INTERESTED", Label = "Interested",       IsSale = false, IsContact = true,  IsRetry = true,  IsActive = true },
            new WrapUpCode { AgencyId = agency.Id, Code = "NOT_INT",   Label = "Not interested",    IsSale = false, IsContact = true,  IsRetry = false, IsActive = true },
            new WrapUpCode { AgencyId = agency.Id, Code = "CALLBACK",  Label = "Schedule callback", IsSale = false, IsContact = true,  IsRetry = true,  IsActive = true },
            new WrapUpCode { AgencyId = agency.Id, Code = "VM",        Label = "Voicemail left",    IsSale = false, IsContact = false, IsRetry = true,  IsActive = true },
            new WrapUpCode { AgencyId = agency.Id, Code = "NO_ANS",    Label = "No answer",         IsSale = false, IsContact = false, IsRetry = true,  IsActive = true },
            new WrapUpCode { AgencyId = agency.Id, Code = "WRONG_NUM", Label = "Wrong number",      IsSale = false, IsContact = false, IsRetry = false, IsActive = true },
            new WrapUpCode { AgencyId = agency.Id, Code = "DNC",       Label = "Do not call",       IsSale = false, IsContact = true,  IsRetry = false, IsActive = true });
        await db.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // DNC entries
    // -------------------------------------------------------------------------

    private static async Task SeedDncAsync(AppDbContext db, Agency agency, Random rnd)
    {
        if (await db.DncEntries.AnyAsync(d => d.AgencyId == agency.Id)) return;
        var sources = new[] { "Internal", "FederalDNC", "StateDNC" };
        var reasons = new[] {
            "Customer requested DNC",
            "Filed complaint",
            "Wrong number reported",
            "Imported from federal list",
            "Litigator flagged",
        };
        var list = new List<DncEntry>();
        for (int i = 0; i < 30; i++)
        {
            var phone = $"+1{rnd.Next(200, 999)}{rnd.Next(200, 999):D3}{rnd.Next(0, 9999):D4}";
            var hasExpiry = rnd.NextDouble() > 0.6;
            list.Add(new DncEntry
            {
                AgencyId = agency.Id,
                PhoneNormalized = phone,
                Reason = reasons[rnd.Next(reasons.Length)],
                Source = sources[rnd.Next(sources.Length)],
                ExpiresAt = hasExpiry ? DateTime.UtcNow.AddDays(rnd.Next(30, 730)) : null,
            });
        }
        db.DncEntries.AddRange(list);
        await db.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // Scripts
    // -------------------------------------------------------------------------

    private static async Task SeedScriptsAsync(AppDbContext db, Agency agency)
    {
        if (await db.Scripts.AnyAsync(s => s.AgencyId == agency.Id)) return;
        var scripts = new[]
        {
            new Script
            {
                AgencyId = agency.Id, Name = "ACA Front Pitch",
                Stage = WorkflowStage.New, Role = Roles.Fronter, Version = 1, IsActive = true,
                Body = "Hi, this is {{agentName}} with Apex Health. I'm calling about your interest in affordable health coverage in {{state}}.\n\nDo you have a couple of minutes to see what subsidies you qualify for?",
            },
            new Script
            {
                AgencyId = agency.Id, Name = "Verification Pitch",
                Stage = WorkflowStage.Fronted, Role = Roles.Verifier, Version = 1, IsActive = true,
                Body = "Hi {{firstName}}, this is the verification team. Just confirming a few details before we connect you with a licensed agent.\n\n1. Full legal name?\n2. Date of birth?\n3. ZIP code?\n4. Annual household income?\n5. Anyone else on the policy?",
            },
            new Script
            {
                AgencyId = agency.Id, Name = "Closer — Plan Comparison",
                Stage = WorkflowStage.Verified, Role = Roles.Closer, Version = 2, IsActive = true,
                Body = "Great news {{firstName}} — based on what you shared, here are the top three plans for you:\n\n• Carrier A: ${{premiumA}}/mo, $0 deductible\n• Carrier B: ${{premiumB}}/mo, telehealth included\n• Carrier C: ${{premiumC}}/mo, lowest premium\n\nWhich one fits best?",
            },
            new Script
            {
                AgencyId = agency.Id, Name = "Voicemail Drop",
                Stage = null, Role = null, Version = 1, IsActive = true,
                Body = "Hi {{firstName}}, this is {{agentName}} from Apex Health. I had your information about ACA coverage — give me a call back at (555) 123-4567 when you have a moment.",
            },
            new Script
            {
                AgencyId = agency.Id, Name = "Winback — 60 Day",
                Stage = WorkflowStage.Winback, Role = Roles.Winbacks, Version = 1, IsActive = true,
                Body = "Hi {{firstName}}, this is {{agentName}}. We spoke a couple of months ago about coverage. Special enrollment is open — has anything changed for you and the family?",
            },
            new Script
            {
                AgencyId = agency.Id, Name = "Followup — Interested",
                Stage = WorkflowStage.Followup, Role = Roles.Followups, Version = 1, IsActive = true,
                Body = "Hi {{firstName}}, this is {{agentName}} circling back. Last time we talked you wanted to think about it — any questions I can answer right now?",
            },
        };
        db.Scripts.AddRange(scripts);
        await db.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // Knowledge base
    // -------------------------------------------------------------------------

    private static async Task SeedKnowledgeAsync(AppDbContext db, Agency agency, List<ApplicationUser> users)
    {
        if (await db.KnowledgeArticles.AnyAsync(k => k.AgencyId == agency.Id)) return;
        var author = users.First();
        var now = DateTime.UtcNow;
        var articles = new[]
        {
            new KnowledgeArticle
            {
                AgencyId = agency.Id, AuthorUserId = author.Id, Slug = "getting-started",
                Title = "Getting started with Apex CRM",
                Category = "Onboarding", Tags = "onboarding,basics,new-hire",
                Body = "# Welcome\n\nThis CRM unifies your floor's pipeline, dialer, callbacks, sales, QA, and commissions.\n\n## First steps\n1. Clock in via the Agent Panel\n2. Pick a status (Available)\n3. Take inbound calls or pull from My Queue\n4. Wrap up every call before going Available again",
                IsPublished = true, PublishedAt = now.AddDays(-90), ViewCount = 128,
            },
            new KnowledgeArticle
            {
                AgencyId = agency.Id, AuthorUserId = author.Id, Slug = "dnc-rules",
                Title = "Do-Not-Call rules every agent must follow",
                Category = "Compliance", Tags = "compliance,dnc,tcpa",
                Body = "Never dial a number flagged on the federal or internal DNC list. The dialer will block automatically, but you should:\n\n- Mark customer requests as DNC immediately\n- Use the SCRUB column on imports\n- Never call before 8am or after 9pm local time",
                IsPublished = true, PublishedAt = now.AddDays(-45), ViewCount = 312,
            },
            new KnowledgeArticle
            {
                AgencyId = agency.Id, AuthorUserId = author.Id, Slug = "objection-handling",
                Title = "Top 5 objections and how to handle them",
                Category = "Sales", Tags = "sales,closing,objections",
                Body = "## 1. \"I need to think about it\"\nAcknowledge, then ask one specific question to narrow what they're thinking about.\n\n## 2. \"It's too expensive\"\nReframe in terms of monthly value, then compare to current spend.\n\n## 3. \"I need to ask my spouse\"\nOffer to do a 3-way call right now while everything is fresh.\n\n## 4. \"Send me information\"\nDo it, then book the follow-up before hanging up.\n\n## 5. \"Not now\"\nFind out what would have to change for it to be a yes.",
                IsPublished = true, PublishedAt = now.AddDays(-30), ViewCount = 451,
            },
            new KnowledgeArticle
            {
                AgencyId = agency.Id, AuthorUserId = author.Id, Slug = "carrier-cheatsheet",
                Title = "Carrier cheatsheet — Aetna, UHC, Cigna, Humana",
                Category = "Reference", Tags = "carriers,plans,reference",
                Body = "Quick reference for the four major carriers we contract with. Includes premium ranges, network sizes, telehealth perks, and dental/vision riders.",
                IsPublished = true, PublishedAt = now.AddDays(-14), ViewCount = 89,
            },
            new KnowledgeArticle
            {
                AgencyId = agency.Id, AuthorUserId = author.Id, Slug = "wrap-up-codes",
                Title = "When to use which wrap-up code",
                Category = "Operations", Tags = "wrap-up,disposition,operations",
                Body = "Every call must end with a wrap-up. Use SALE only when the policy is bound and the customer confirmed verbally. Use CALLBACK with a specific time. Never use NO_ANS for a hostile contact — that's a NOT_INT.",
                IsPublished = true, PublishedAt = now.AddDays(-7), ViewCount = 56,
            },
            new KnowledgeArticle
            {
                AgencyId = agency.Id, AuthorUserId = author.Id, Slug = "qa-process",
                Title = "How QA scoring works (DRAFT)",
                Category = "QA", Tags = "qa,scoring,coaching",
                Body = "DRAFT — outline of the QA process, rubric structure, and coaching cadence.",
                IsPublished = false, ViewCount = 4,
            },
        };
        db.KnowledgeArticles.AddRange(articles);
        await db.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // QA — rubrics and reviews
    // -------------------------------------------------------------------------

    private static async Task SeedQaAsync(AppDbContext db, Agency agency, List<ApplicationUser> agents, List<ApplicationUser> reviewers, Random rnd)
    {
        if (await db.QaRubrics.AnyAsync(r => r.AgencyId == agency.Id)) return;

        var rubric = new QaRubric
        {
            AgencyId = agency.Id, Name = "Inbound Sales Call",
            Description = "Standard rubric for sales calls. Total 100 points.", IsActive = true,
        };
        db.QaRubrics.Add(rubric);
        await db.SaveChangesAsync();

        var items = new[]
        {
            new QaRubricItem { AgencyId = agency.Id, RubricId = rubric.Id, Label = "Greeting & introduction",       MaxScore = 10, Order = 1 },
            new QaRubricItem { AgencyId = agency.Id, RubricId = rubric.Id, Label = "Used customer name",            MaxScore = 5,  Order = 2 },
            new QaRubricItem { AgencyId = agency.Id, RubricId = rubric.Id, Label = "Active listening",              MaxScore = 15, Order = 3 },
            new QaRubricItem { AgencyId = agency.Id, RubricId = rubric.Id, Label = "Disclosed company / agent",     MaxScore = 10, Order = 4 },
            new QaRubricItem { AgencyId = agency.Id, RubricId = rubric.Id, Label = "Compliance disclosures",        MaxScore = 20, Order = 5 },
            new QaRubricItem { AgencyId = agency.Id, RubricId = rubric.Id, Label = "Plan presentation accuracy",    MaxScore = 15, Order = 6 },
            new QaRubricItem { AgencyId = agency.Id, RubricId = rubric.Id, Label = "Objection handling",            MaxScore = 10, Order = 7 },
            new QaRubricItem { AgencyId = agency.Id, RubricId = rubric.Id, Label = "Closing & next-step booking",   MaxScore = 10, Order = 8 },
            new QaRubricItem { AgencyId = agency.Id, RubricId = rubric.Id, Label = "Wrap-up correctness",           MaxScore = 5,  Order = 9 },
        };
        db.QaRubricItems.AddRange(items);

        // A second rubric for follow-ups
        var followupRubric = new QaRubric
        {
            AgencyId = agency.Id, Name = "Follow-up Call",
            Description = "Lighter rubric used for follow-ups, callbacks, and winbacks.", IsActive = true,
        };
        db.QaRubrics.Add(followupRubric);
        await db.SaveChangesAsync();

        db.QaRubricItems.AddRange(
            new QaRubricItem { AgencyId = agency.Id, RubricId = followupRubric.Id, Label = "Re-introduction",         MaxScore = 10, Order = 1 },
            new QaRubricItem { AgencyId = agency.Id, RubricId = followupRubric.Id, Label = "Recap previous call",     MaxScore = 15, Order = 2 },
            new QaRubricItem { AgencyId = agency.Id, RubricId = followupRubric.Id, Label = "Asked clarifying questions", MaxScore = 25, Order = 3 },
            new QaRubricItem { AgencyId = agency.Id, RubricId = followupRubric.Id, Label = "Booked next step",        MaxScore = 25, Order = 4 },
            new QaRubricItem { AgencyId = agency.Id, RubricId = followupRubric.Id, Label = "Wrap-up correctness",     MaxScore = 25, Order = 5 });
        await db.SaveChangesAsync();

        // Generate reviews against recent sales — only if we have at least one
        var sales = await db.Sales.Where(s => s.AgencyId == agency.Id).Take(20).ToListAsync();
        if (sales.Count == 0 || agents.Count == 0 || reviewers.Count == 0) return;

        var ruItems = await db.QaRubricItems.Where(i => i.RubricId == rubric.Id).ToListAsync();
        int totalMax = ruItems.Sum(i => i.MaxScore);

        var reviews = new List<QaReview>();
        var reviewItems = new List<QaReviewItem>();
        foreach (var sale in sales.Take(15))
        {
            var reviewer = reviewers[rnd.Next(reviewers.Count)];
            var review = new QaReview
            {
                AgencyId = agency.Id,
                LeadId = sale.LeadId, SaleId = sale.Id,
                AgentUserId = sale.CloserUserId,
                ReviewerUserId = reviewer.Id,
                RubricId = rubric.Id,
                MaxScore = totalMax,
                ReviewedAt = sale.SoldAt.AddDays(rnd.Next(1, 5)),
                Notes = rnd.NextDouble() > 0.5
                    ? "Solid call. Clear pace, good objection handling. Could improve on disclosures."
                    : null,
            };
            int total = 0;
            foreach (var it in ruItems)
            {
                var raw = (int)Math.Round(it.MaxScore * (0.6 + rnd.NextDouble() * 0.4));
                total += raw;
                reviewItems.Add(new QaReviewItem
                {
                    AgencyId = agency.Id, ReviewId = review.Id, RubricItemId = it.Id, Score = raw,
                    Comment = rnd.NextDouble() > 0.85 ? "Watch the pacing on this section." : null,
                });
            }
            review.TotalScore = total;
            reviews.Add(review);
        }
        db.QaReviews.AddRange(reviews);
        await db.SaveChangesAsync();
        db.QaReviewItems.AddRange(reviewItems);
        await db.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // Cadences (sequences) + sample enrollments
    // -------------------------------------------------------------------------

    private static async Task SeedCadencesAsync(AppDbContext db, Agency agency)
    {
        if (await db.Cadences.AnyAsync(c => c.AgencyId == agency.Id)) return;

        var newLead = new Cadence
        {
            AgencyId = agency.Id, Name = "New lead — 7-touch",
            Description = "Standard outreach for fresh leads — phone, then SMS, then email over 4 days.",
            IsActive = true,
        };
        var winback = new Cadence
        {
            AgencyId = agency.Id, Name = "Winback — 30 day",
            Description = "Re-engage leads marked Lost in the past 30 days.", IsActive = true,
        };
        var followup = new Cadence
        {
            AgencyId = agency.Id, Name = "Post-quote follow-up",
            Description = "Used after a closer presents a quote and customer asks to think.", IsActive = true,
        };
        db.Cadences.AddRange(newLead, winback, followup);
        await db.SaveChangesAsync();

        db.CadenceSteps.AddRange(
            // New lead 7-touch
            new CadenceStep { AgencyId = agency.Id, CadenceId = newLead.Id, Order = 1, StepKind = "Call",  DelayMinutes = 0,    StopIfContacted = true,  ParametersJson = "{}" },
            new CadenceStep { AgencyId = agency.Id, CadenceId = newLead.Id, Order = 2, StepKind = "Sms",   DelayMinutes = 60,   StopIfContacted = true,  ParametersJson = "{\"template\":\"intro-sms\"}" },
            new CadenceStep { AgencyId = agency.Id, CadenceId = newLead.Id, Order = 3, StepKind = "Wait",  DelayMinutes = 240,  StopIfContacted = true,  ParametersJson = "{}" },
            new CadenceStep { AgencyId = agency.Id, CadenceId = newLead.Id, Order = 4, StepKind = "Call",  DelayMinutes = 0,    StopIfContacted = true,  ParametersJson = "{}" },
            new CadenceStep { AgencyId = agency.Id, CadenceId = newLead.Id, Order = 5, StepKind = "Email", DelayMinutes = 1440, StopIfContacted = true,  ParametersJson = "{\"template\":\"benefits-email\"}" },
            new CadenceStep { AgencyId = agency.Id, CadenceId = newLead.Id, Order = 6, StepKind = "Call",  DelayMinutes = 1440, StopIfContacted = true,  ParametersJson = "{}" },
            new CadenceStep { AgencyId = agency.Id, CadenceId = newLead.Id, Order = 7, StepKind = "Sms",   DelayMinutes = 1440, StopIfContacted = false, ParametersJson = "{\"template\":\"final-sms\"}" },

            // Winback
            new CadenceStep { AgencyId = agency.Id, CadenceId = winback.Id, Order = 1, StepKind = "Email", DelayMinutes = 0,    StopIfContacted = true,  ParametersJson = "{\"template\":\"winback-email\"}" },
            new CadenceStep { AgencyId = agency.Id, CadenceId = winback.Id, Order = 2, StepKind = "Wait",  DelayMinutes = 4320, StopIfContacted = true,  ParametersJson = "{}" },
            new CadenceStep { AgencyId = agency.Id, CadenceId = winback.Id, Order = 3, StepKind = "Call",  DelayMinutes = 0,    StopIfContacted = true,  ParametersJson = "{}" },

            // Post-quote follow-up
            new CadenceStep { AgencyId = agency.Id, CadenceId = followup.Id, Order = 1, StepKind = "Sms",   DelayMinutes = 30,   StopIfContacted = true,  ParametersJson = "{\"template\":\"thanks-for-time\"}" },
            new CadenceStep { AgencyId = agency.Id, CadenceId = followup.Id, Order = 2, StepKind = "Call",  DelayMinutes = 1440, StopIfContacted = true,  ParametersJson = "{}" },
            new CadenceStep { AgencyId = agency.Id, CadenceId = followup.Id, Order = 3, StepKind = "Email", DelayMinutes = 4320, StopIfContacted = false, ParametersJson = "{\"template\":\"final-quote-recap\"}" });
        await db.SaveChangesAsync();

        // Enroll a handful of New / Followup leads into the relevant cadence
        var newLeads = await db.Leads.Where(l => l.AgencyId == agency.Id && l.Stage == WorkflowStage.New).Take(8).ToListAsync();
        var followupLeads = await db.Leads.Where(l => l.AgencyId == agency.Id && l.Stage == WorkflowStage.Followup).Take(4).ToListAsync();
        var enrollments = new List<CadenceEnrollment>();
        foreach (var l in newLeads)
        {
            enrollments.Add(new CadenceEnrollment
            {
                AgencyId = agency.Id, CadenceId = newLead.Id, LeadId = l.Id,
                CurrentStepOrder = 1, EnrolledAt = DateTime.UtcNow.AddHours(-Random.Shared.Next(1, 48)),
                NextRunAt = DateTime.UtcNow.AddMinutes(Random.Shared.Next(15, 1440)),
                Status = "Active",
            });
        }
        foreach (var l in followupLeads)
        {
            enrollments.Add(new CadenceEnrollment
            {
                AgencyId = agency.Id, CadenceId = followup.Id, LeadId = l.Id,
                CurrentStepOrder = 2, EnrolledAt = DateTime.UtcNow.AddDays(-Random.Shared.Next(1, 5)),
                NextRunAt = DateTime.UtcNow.AddMinutes(Random.Shared.Next(60, 1440)),
                Status = "Active",
            });
        }
        db.CadenceEnrollments.AddRange(enrollments);
        await db.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // Lead lists + memberships + a sample import batch
    // -------------------------------------------------------------------------

    private static async Task SeedLeadListsAsync(AppDbContext db, Agency agency, List<ApplicationUser> users, Random rnd)
    {
        if (await db.LeadLists.AnyAsync(l => l.AgencyId == agency.Id)) return;
        var initiator = users.First();

        var lists = new[]
        {
            new LeadList { AgencyId = agency.Id, Name = "Q1 ACA Florida",      Description = "Florida ACA prospects from Q1 spend.",     IsActive = true },
            new LeadList { AgencyId = agency.Id, Name = "Medicare Texas",      Description = "Medicare Advantage list — Texas.",         IsActive = true },
            new LeadList { AgencyId = agency.Id, Name = "Final Expense — 60+", Description = "Final-expense leads aged 60+.",            IsActive = true },
            new LeadList { AgencyId = agency.Id, Name = "Returning leads",     Description = "Lost leads worth a winback try.",          IsActive = false },
        };
        db.LeadLists.AddRange(lists);
        await db.SaveChangesAsync();

        // Bucket existing leads into the lists evenly
        var allLeads = await db.Leads.Where(l => l.AgencyId == agency.Id).ToListAsync();
        var memberships = new List<LeadListMembership>();
        foreach (var l in allLeads)
        {
            var listIdx = rnd.Next(lists.Length);
            memberships.Add(new LeadListMembership { AgencyId = agency.Id, LeadListId = lists[listIdx].Id, LeadId = l.Id });
        }
        db.LeadListMemberships.AddRange(memberships);

        // Update LeadCount
        foreach (var list in lists)
            list.LeadCount = memberships.Count(m => m.LeadListId == list.Id);
        await db.SaveChangesAsync();

        // Sample import batches
        db.LeadImportBatches.AddRange(
            new LeadImportBatch
            {
                AgencyId = agency.Id, LeadListId = lists[0].Id, FileName = "Q1-FL-leads.csv",
                TotalRows = 525, Imported = 487, Duplicates = 22, DncScrubbed = 14, Errors = 2,
                Status = "Completed", InitiatedByUserId = initiator.Id,
                CompletedAt = DateTime.UtcNow.AddDays(-12), CreatedAt = DateTime.UtcNow.AddDays(-12),
            },
            new LeadImportBatch
            {
                AgencyId = agency.Id, LeadListId = lists[1].Id, FileName = "medicare-tx-feb.csv",
                TotalRows = 318, Imported = 296, Duplicates = 8, DncScrubbed = 12, Errors = 2,
                Status = "Completed", InitiatedByUserId = initiator.Id,
                CompletedAt = DateTime.UtcNow.AddDays(-5), CreatedAt = DateTime.UtcNow.AddDays(-5),
            },
            new LeadImportBatch
            {
                AgencyId = agency.Id, LeadListId = lists[2].Id, FileName = "final-exp-60-up.csv",
                TotalRows = 142, Imported = 138, Duplicates = 1, DncScrubbed = 3, Errors = 0,
                Status = "Completed", InitiatedByUserId = initiator.Id,
                CompletedAt = DateTime.UtcNow.AddHours(-18), CreatedAt = DateTime.UtcNow.AddHours(-18),
            });
        await db.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // Workflow rules + executions
    // -------------------------------------------------------------------------

    private static async Task SeedWorkflowsAsync(AppDbContext db, Agency agency, Random rnd)
    {
        if (await db.WorkflowRules.AnyAsync(w => w.AgencyId == agency.Id)) return;

        var assignNew = new WorkflowRule
        {
            AgencyId = agency.Id, Name = "Auto-assign new leads to a Fronter",
            EventType = "lead.created", Priority = 100, IsActive = true, ContinueOnError = true,
            Description = "When a new lead lands, assign it round-robin to an active Fronter.",
            ConditionJson = null,
        };
        var welcomeSms = new WorkflowRule
        {
            AgencyId = agency.Id, Name = "Welcome SMS for high-score leads",
            EventType = "lead.created", Priority = 200, IsActive = true, ContinueOnError = true,
            Description = "If lead score is at least 40, fire off a welcome SMS immediately.",
            ConditionJson = "{\"all\":[{\"fact\":\"score\",\"op\":\"gte\",\"value\":40}]}",
        };
        var soldNotify = new WorkflowRule
        {
            AgencyId = agency.Id, Name = "Notify TL when a sale closes",
            EventType = "sale.closed", Priority = 100, IsActive = true, ContinueOnError = true,
            Description = "Pings the team lead in chat when a sale is recorded.",
        };
        var winbackNudge = new WorkflowRule
        {
            AgencyId = agency.Id, Name = "Winback callback after 30 days lost",
            EventType = "lead.transitioned", Priority = 150, IsActive = false, ContinueOnError = true,
            Description = "Schedule a callback 30 days after a lead is marked Lost.",
            ConditionJson = "{\"all\":[{\"fact\":\"toStage\",\"op\":\"eq\",\"value\":\"Lost\"}]}",
        };
        db.WorkflowRules.AddRange(assignNew, welcomeSms, soldNotify, winbackNudge);
        await db.SaveChangesAsync();

        db.WorkflowActions.AddRange(
            new WorkflowAction { AgencyId = agency.Id, RuleId = assignNew.Id,    ActionType = "assign-agent", Order = 1, ParametersJson = "{\"strategy\":\"round-robin\",\"role\":\"Fronter\"}" },
            new WorkflowAction { AgencyId = agency.Id, RuleId = welcomeSms.Id,   ActionType = "send-sms",     Order = 1, ParametersJson = "{\"template\":\"welcome-sms\"}" },
            new WorkflowAction { AgencyId = agency.Id, RuleId = soldNotify.Id,   ActionType = "notify-user",  Order = 1, ParametersJson = "{\"role\":\"TeamLead\",\"channel\":\"chat\"}" },
            new WorkflowAction { AgencyId = agency.Id, RuleId = winbackNudge.Id, ActionType = "create-callback", Order = 1, ParametersJson = "{\"delayDays\":30,\"reason\":\"30-day winback\"}" });
        await db.SaveChangesAsync();

        // Sample executions
        var execs = new List<WorkflowExecution>();
        for (int i = 0; i < 25; i++)
        {
            var rule = new[] { assignNew, welcomeSms, soldNotify }[rnd.Next(3)];
            var status = rnd.NextDouble() > 0.85 ? "Failed" : "Succeeded";
            var startedAt = DateTime.UtcNow.AddMinutes(-rnd.Next(1, 60 * 24 * 5));
            execs.Add(new WorkflowExecution
            {
                AgencyId = agency.Id, RuleId = rule.Id, EventType = rule.EventType,
                PayloadJson = "{\"sample\":true}", StartedAt = startedAt,
                CompletedAt = startedAt.AddSeconds(rnd.Next(1, 8)),
                Status = status,
                Error = status == "Failed" ? "Downstream provider returned 503 Service Unavailable" : null,
            });
        }
        db.WorkflowExecutions.AddRange(execs);
        await db.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // Inbound queues + IVR + voicemail + public endpoint
    // -------------------------------------------------------------------------

    private static async Task SeedInboundQueuesAsync(AppDbContext db, Agency agency, List<ApplicationUser> users)
    {
        if (await db.InboundQueues.AnyAsync(q => q.AgencyId == agency.Id)) return;
        var initiator = users.First();

        var voicemail = new VoicemailAsset
        {
            AgencyId = agency.Id, Name = "After-hours greeting",
            Url = "https://cdn.apexcrm.local/audio/after-hours.mp3",
            DurationSeconds = 28, IsActive = true, CreatedByUserId = initiator.Id,
        };
        var voicemailDrop = new VoicemailAsset
        {
            AgencyId = agency.Id, Name = "Standard agent drop",
            Url = "https://cdn.apexcrm.local/audio/agent-drop.mp3",
            DurationSeconds = 22, IsActive = true, CreatedByUserId = initiator.Id,
        };
        db.VoicemailAssets.AddRange(voicemail, voicemailDrop);
        await db.SaveChangesAsync();

        var generalQueue = new InboundQueue
        {
            AgencyId = agency.Id, Name = "Inbound — General",
            PhoneNumber = "+18005551001", Strategy = "longest-idle", MaxWaitSeconds = 120,
            VoicemailAssetId = voicemail.Id, IsActive = true,
        };
        var spanishQueue = new InboundQueue
        {
            AgencyId = agency.Id, Name = "Inbound — Spanish",
            PhoneNumber = "+18005551002", RequiredSkillCode = "ES",
            Strategy = "longest-idle", MaxWaitSeconds = 90, IsActive = true,
        };
        var medicareQueue = new InboundQueue
        {
            AgencyId = agency.Id, Name = "Inbound — Medicare",
            PhoneNumber = "+18005551003", RequiredSkillCode = "MED",
            Strategy = "longest-idle", MaxWaitSeconds = 90, IsActive = true,
        };
        db.InboundQueues.AddRange(generalQueue, spanishQueue, medicareQueue);
        await db.SaveChangesAsync();

        // IVR menu on the general queue
        var ivr = new IvrMenu
        {
            AgencyId = agency.Id, InboundQueueId = generalQueue.Id, Name = "Main Menu",
            Greeting = "Thanks for calling Apex Health. Press 1 for English, 2 for Spanish, or 3 for Medicare.",
        };
        db.IvrMenus.Add(ivr);
        await db.SaveChangesAsync();
        db.IvrOptions.AddRange(
            new IvrOption { AgencyId = agency.Id, IvrMenuId = ivr.Id, Order = 1, DigitOrSpeech = "1", Label = "English",  ActionType = "RouteToQueue", ActionTargetId = generalQueue.Id.ToString() },
            new IvrOption { AgencyId = agency.Id, IvrMenuId = ivr.Id, Order = 2, DigitOrSpeech = "2", Label = "Spanish",  ActionType = "RouteToQueue", ActionTargetId = spanishQueue.Id.ToString() },
            new IvrOption { AgencyId = agency.Id, IvrMenuId = ivr.Id, Order = 3, DigitOrSpeech = "3", Label = "Medicare", ActionType = "RouteToQueue", ActionTargetId = medicareQueue.Id.ToString() });
        await db.SaveChangesAsync();

        // Public lead-capture endpoints (no real secret — placeholder hash)
        if (!await db.PublicLeadCaptureEndpoints.AnyAsync(p => p.AgencyId == agency.Id))
        {
            db.PublicLeadCaptureEndpoints.AddRange(
                new PublicLeadCaptureEndpoint
                {
                    AgencyId = agency.Id, Slug = "aca-quote-form", IsActive = true, LeadCount = 124,
                    SecretHash = "demo-hash-aca",
                },
                new PublicLeadCaptureEndpoint
                {
                    AgencyId = agency.Id, Slug = "medicare-callback", IsActive = true, LeadCount = 38,
                    SecretHash = "demo-hash-medicare",
                });
            await db.SaveChangesAsync();
        }
    }

    // -------------------------------------------------------------------------
    // Chat — a couple of rooms + messages
    // -------------------------------------------------------------------------

    private static async Task SeedChatAsync(AppDbContext db, Agency agency, List<ApplicationUser> users, Random rnd)
    {
        if (await db.ChatRooms.AnyAsync(r => r.AgencyId == agency.Id)) return;
        if (users.Count < 2) return;

        var general = new ChatRoom { AgencyId = agency.Id, Name = "general", IsDirect = false };
        var leadsRoom = new ChatRoom { AgencyId = agency.Id, Name = "leads-floor", IsDirect = false };
        var management = new ChatRoom { AgencyId = agency.Id, Name = "management", IsDirect = false };
        db.ChatRooms.AddRange(general, leadsRoom, management);
        await db.SaveChangesAsync();

        // Members
        var generalMembers = users.Select(u => new ChatRoomMember { AgencyId = agency.Id, RoomId = general.Id, UserId = u.Id }).ToList();
        var leadsMembers = users.Take(Math.Min(8, users.Count))
            .Select(u => new ChatRoomMember { AgencyId = agency.Id, RoomId = leadsRoom.Id, UserId = u.Id }).ToList();
        var mgmtMembers = users.Take(Math.Min(3, users.Count))
            .Select(u => new ChatRoomMember { AgencyId = agency.Id, RoomId = management.Id, UserId = u.Id }).ToList();
        db.ChatRoomMembers.AddRange(generalMembers);
        db.ChatRoomMembers.AddRange(leadsMembers);
        db.ChatRoomMembers.AddRange(mgmtMembers);
        await db.SaveChangesAsync();

        // Sample messages
        var general_lines = new[]
        {
            ("Morning team! Big day ahead — let's hit the targets.", 0),
            ("Floor's looking good. 12 inbound queued for Spanish line.", 1),
            ("Reminder: new objection-handling KB article posted.", 0),
            ("Anyone free for a 3-way on a Florida ACA quote?", 2),
            ("On it.", 0),
        };
        var leads_lines = new[]
        {
            ("Hot lead just dropped — score 87, FL ACA, family of 4.", 0),
            ("Grabbing it.", 1),
            ("Marked Sold — premium $312/mo, Aetna PPO.", 1),
            ("Nice. Validator queue is clear.", 2),
        };
        var mgmt_lines = new[]
        {
            ("QA scorecard for last week is up — overall 91%.", 0),
            ("Two coaching opportunities this week, scheduling 1:1s.", 1),
        };

        async Task SendBatch(ChatRoom room, IReadOnlyList<ChatRoomMember> members, IEnumerable<(string body, int senderIdx)> lines, int dayOffset)
        {
            var msgs = new List<ChatMessage>();
            int i = 0;
            foreach (var (body, idx) in lines)
            {
                var sender = members[idx % members.Count].UserId;
                msgs.Add(new ChatMessage
                {
                    AgencyId = agency.Id, RoomId = room.Id, SenderUserId = sender, Body = body,
                    SentAt = DateTime.UtcNow.AddDays(-dayOffset).AddMinutes(-i * (5 + rnd.Next(0, 20))),
                });
                i++;
            }
            db.ChatMessages.AddRange(msgs);
            await db.SaveChangesAsync();
        }

        await SendBatch(general, generalMembers, general_lines, 0);
        await SendBatch(leadsRoom, leadsMembers, leads_lines, 0);
        await SendBatch(management, mgmtMembers, mgmt_lines, 1);
    }

    // -------------------------------------------------------------------------
    // Call records — populates Recent Calls + supports QA/wrap-up flows
    // -------------------------------------------------------------------------

    private static async Task SeedCallHistoryAsync(AppDbContext db, Agency agency, List<ApplicationUser> agents, Random rnd)
    {
        if (await db.CallRecords.AnyAsync(c => c.AgencyId == agency.Id)) return;
        var leads = await db.Leads.Where(l => l.AgencyId == agency.Id).Take(80).ToListAsync();
        if (leads.Count == 0 || agents.Count == 0) return;

        var wrapCodes = await db.WrapUpCodes.Where(w => w.AgencyId == agency.Id).Select(w => w.Code).ToListAsync();
        if (wrapCodes.Count == 0) wrapCodes = new List<string> { "INTERESTED", "NO_ANS", "VM" };

        var statuses = new[] { "Completed", "NoAnswer", "Voicemail", "Abandoned" };
        var directions = new[] { "Outbound", "Inbound", "Outbound", "Outbound" }; // weighted

        var calls = new List<CallRecord>();
        for (int i = 0; i < 120; i++)
        {
            var lead = leads[rnd.Next(leads.Count)];
            var agent = agents[rnd.Next(agents.Count)];
            var initiated = DateTime.UtcNow.AddMinutes(-rnd.Next(1, 60 * 24 * 7));
            var answered = rnd.NextDouble() > 0.35 ? initiated.AddSeconds(rnd.Next(3, 30)) : (DateTime?)null;
            var ended = answered is not null ? answered.Value.AddSeconds(rnd.Next(30, 600)) : initiated.AddSeconds(rnd.Next(20, 90));
            var direction = directions[rnd.Next(directions.Length)];
            var status = answered is null ? "NoAnswer" : statuses[rnd.Next(statuses.Length)];
            var wrap = answered is not null && rnd.NextDouble() > 0.10 ? wrapCodes[rnd.Next(wrapCodes.Count)] : null;

            calls.Add(new CallRecord
            {
                AgencyId = agency.Id, LeadId = lead.Id, AgentUserId = agent.Id,
                Provider = "ViciDialer",
                ProviderCallId = $"VICI-{rnd.Next(100000, 999999)}",
                Status = status, Direction = direction,
                InitiatedAt = initiated, AnsweredAt = answered, EndedAt = ended,
                RecordingUrl = answered is not null && rnd.NextDouble() > 0.5
                    ? $"https://cdn.apexcrm.local/recordings/{Guid.NewGuid():N}.mp3"
                    : null,
                WrapUpCode = wrap,
                Notes = wrap is not null && rnd.NextDouble() > 0.7
                    ? "Customer asked to be called back next week."
                    : null,
            });
        }
        db.CallRecords.AddRange(calls);
        await db.SaveChangesAsync();
    }
}
