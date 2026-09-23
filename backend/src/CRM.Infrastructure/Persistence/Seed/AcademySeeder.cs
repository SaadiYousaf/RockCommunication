using CRM.Domain.Entities;
using CRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Roles = CRM.Domain.Enums.Roles;

namespace CRM.Infrastructure.Persistence.Seed;

/// <summary>
/// The Learning Academy curriculum, seeded with the application.
///
/// WHY CODE-FIRST RATHER THAN AN AUTHORING UI: these lessons describe how THIS product works, so they
/// change when the product changes — in the same commit, reviewed in the same pull request. Content
/// living in a database that nobody diffs is how documentation silently goes stale and starts telling
/// new hires about screens that no longer exist. An admin authoring UI is worth adding later for
/// agency-specific material; the product's own manual belongs next to the product.
///
/// Idempotent: everything upserts on its stable Key, so a restart updates the wording in place and
/// never duplicates a course or loses anyone's progress (progress points at lesson ids, which are
/// preserved across re-seeds).
/// </summary>
internal static class AcademySeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        foreach (var spec in Curriculum())
        {
            var course = await db.AcademyCourses
                .Include(c => c.Lessons)
                .FirstOrDefaultAsync(c => c.Key == spec.Key);

            if (course is null)
            {
                course = new AcademyCourse { Key = spec.Key };
                db.AcademyCourses.Add(course);
            }

            course.Title = spec.Title;
            course.Summary = spec.Summary;
            course.Icon = spec.Icon;
            course.Level = spec.Level;
            course.Order = spec.Order;
            course.AudienceRolesCsv = string.Join(",", spec.Audience);
            course.IsPublished = true;

            await db.SaveChangesAsync();

            for (var i = 0; i < spec.Lessons.Count; i++)
            {
                var ls = spec.Lessons[i];
                var lesson = await db.AcademyLessons
                    .FirstOrDefaultAsync(l => l.CourseId == course.Id && l.Key == ls.Key);

                if (lesson is null)
                {
                    lesson = new AcademyLesson { CourseId = course.Id, Key = ls.Key };
                    db.AcademyLessons.Add(lesson);
                }

                lesson.Title = ls.Title;
                lesson.Summary = ls.Summary;
                lesson.Order = i;
                lesson.EstimatedMinutes = ls.Minutes;
                lesson.Route = ls.Route;
                lesson.BodyMarkdown = ls.Body.Trim();
                lesson.IsPublished = true;

                await db.SaveChangesAsync();

                // Questions are replaced wholesale: they are small, and matching them individually
                // would mean carrying a stable key for every question for no benefit.
                var old = await db.AcademyQuizQuestions.Where(q => q.LessonId == lesson.Id).ToListAsync();
                if (old.Count > 0)
                {
                    db.AcademyQuizQuestions.RemoveRange(old);
                    await db.SaveChangesAsync();
                }

                for (var qi = 0; qi < ls.Questions.Count; qi++)
                {
                    var q = ls.Questions[qi];
                    db.AcademyQuizQuestions.Add(new AcademyQuizQuestion
                    {
                        LessonId = lesson.Id,
                        Order = qi,
                        Prompt = q.Prompt,
                        OptionsJson = JsonSerializer.Serialize(q.Options),
                        CorrectIndex = q.CorrectIndex,
                        Explanation = q.Explanation,
                    });
                }
                await db.SaveChangesAsync();
            }
        }
    }

    // ── curriculum definition ────────────────────────────────────────────────

    private record Q(string Prompt, string[] Options, int CorrectIndex, string Explanation);

    private record L(string Key, string Title, string Summary, int Minutes, string? Route, string Body,
        List<Q> Questions);

    private record C(string Key, string Title, string Summary, string Icon, AcademyLevel Level, int Order,
        string[] Audience, List<L> Lessons);

    /// <summary>
    /// Every course here maps to a screen this product actually has. Nothing is invented: if a lesson
    /// names a button, that button exists, and its Route opens the real page.
    /// </summary>
    private static List<C> Curriculum() => new()
    {
        // ── Everyone ─────────────────────────────────────────────────────────
        new C("getting-started", "Getting started",
            "Find your way around, know where your work lives, and get your first day done.",
            "compass", AcademyLevel.Beginner, 0, Array.Empty<string>(), new()
        {
            new L("the-shape-of-the-app", "The shape of the app",
                "What the sidebar sections mean and where to look for things.", 4, "/dashboard",
                """
                ## What you're looking at

                The sidebar groups everything into a few sections. You will not need all of them —
                what you see depends on your role.

                - **Workspace** — your day. Your leads, your callbacks, chat, and the dashboard.
                - **Pipeline** — the customer journey: leads, sales, commission, call history.
                - **Operations** — running the floor: supervision, call centre setup, quality, documents.
                - **Human Resources** — people: employees, attendance, interviews, payroll.
                - **Administration** — the platform itself: agencies, users, roles, audit.

                ### The one thing to remember

                **My Leads is your work.** If a lead is assigned to you, it is there — and only there.
                You never have to hunt through several lists to find what you are supposed to be doing.

                > **Available Leads** is the shared pile. Anything sitting there has no owner yet, and
                > claiming one moves it into My Leads.
                """,
                new() {
                    new Q("Where do you find the leads assigned to you?",
                        new[] { "My Leads", "Available Leads", "All Leads", "The dashboard" }, 0,
                        "My Leads is your workload. Available Leads is the unclaimed pile; All Leads is the searchable database."),
                    new Q("A lead is sitting in Available Leads. Who owns it?",
                        new[] { "The person who created it", "Nobody yet", "The team lead", "Whoever called it last" }, 1,
                        "Available Leads only ever contains unclaimed leads. Claiming one is what gives it an owner."),
                }),

            new L("your-dashboard", "Your dashboard",
                "Reading the Your Work panel and deciding what to do first.", 3, "/dashboard",
                """
                ## Start here every morning

                The **Your work** panel at the top of the dashboard is the short answer to "what do I
                do now". Each tile is a link straight to that work.

                - **My Leads** — assigned to you
                - **Available Leads** — waiting to be picked up
                - **Callbacks** — calls you promised to make
                - **Submissions** — waiting on review

                A tile only appears when it has something in it, so an empty dashboard genuinely means
                there is nothing waiting on you.

                ### Below that

                The rest of the dashboard is context rather than instruction: active leads, sales this
                week, conversion. Useful to glance at, not something you act on directly.
                """,
                new() {
                    new Q("A tile is missing from Your work. What does that mean?",
                        new[] { "It's broken", "There's nothing in it", "You lack permission", "It's still loading" }, 1,
                        "Tiles with a count of zero are hidden, so the panel only ever shows real work."),
                }),

            new L("clocking-in", "Clocking in and out",
                "Why the clock matters and what it drives.", 3, "/agent",
                """
                ## The bar at the top

                Until you clock in, the banner reads **You're off the clock** and you will not receive
                calls. Clocking in starts your shift and makes you available to the dialer.

                Your clocked-in time feeds attendance and the floor wallboard, so it is worth getting
                right. Clock out when you finish — a shift left open overstates your hours.

                > If you forget, a shift is closed automatically once it runs long, and the record notes
                > that the system did it rather than you.
                """,
                new() {
                    new Q("What happens if you never clock out?",
                        new[] { "Nothing", "The shift is closed automatically and marked as such", "Your pay is withheld", "You are logged out" }, 1,
                        "A long-running shift is closed automatically, and the record shows the system did it so attendance stays honest."),
                }),

            new L("getting-help", "Getting help and reporting bugs",
                "Search, the knowledge base, and telling someone when something is wrong.", 3, "/bugs",
                """
                ## Three ways to get unstuck

                1. **Search** — the bar at the top, or `⌘K` / `Ctrl+K`. It jumps to any page as well as
                   finding leads and people.
                2. **Knowledge** — written guidance your agency maintains, under Operations.
                3. **Report a bug** — the button in the bottom-right corner of every page.

                ### Reporting well

                A good report says what you expected, what happened, and which screen you were on. The
                page you were on is captured automatically, so you do not need to describe it.
                """,
                new() {
                    new Q("What is the fastest way to jump to another page?",
                        new[] { "The sidebar", "⌘K / Ctrl+K search", "The browser back button", "The dashboard" }, 1,
                        "The command palette opens with ⌘K or Ctrl+K and jumps to any page you can access."),
                }),
        }),

        // ── Fronters ─────────────────────────────────────────────────────────
        new C("fronting", "Fronting leads",
            "Capture a lead properly and hand it on so the next person can work it.",
            "phone", AcademyLevel.Beginner, 10, new[] { Roles.Fronter }, new()
        {
            new L("what-a-lead-is", "What a lead is here",
                "The pipeline this product runs, stage by stage.", 4, null,
                """
                ## The pipeline

                A lead moves through a fixed set of stages, and each stage belongs to a different job:

                1. **New** — just captured
                2. **Fronted** — you have spoken to them and they are interested
                3. **Verified** — a verifier has confirmed the details
                4. **Closed** — a closer has completed the application and sold a policy
                5. **Validated** → **Funded** — the sale has been checked and the first premium cleared

                Off to the side: **Follow-up**, **Win-back** and **Lost**.

                ### Why the stages matter to you

                Your job ends at **Fronted**. When you front a lead it is released to the verifier
                queue — it stops being yours and becomes available for whoever verifies next. That is
                deliberate: one lead has one owner at a time.
                """,
                new() {
                    new Q("What happens to a lead when you front it?",
                        new[] { "It stays in My Leads", "It's released for a verifier to pick up", "It goes straight to a closer", "It is marked Lost" }, 1,
                        "Fronting hands the lead on. It leaves your list and becomes available to verifiers."),
                    new Q("Which stage comes immediately after Verified?",
                        new[] { "New", "Fronted", "Closed", "Funded" }, 2,
                        "A verified lead is ready for a closer, who completes the application and closes it."),
                }),

            new L("adding-a-lead", "Adding a lead",
                "The intake form, field by field, and why it is typing-only.", 6, "/intake",
                """
                ## Add Lead

                The form is grouped into three parts:

                - **Customer** — who you are speaking to, and their age
                - **Contact details** — how to reach them, and where they live
                - **Consent & source** — proof they agreed to be contacted

                ### Typing only

                Copy and paste is blocked on purpose. Every field has to be typed while you are on the
                call. This keeps the lead genuinely live and compliant, and stops recycled or
                pre-filled data entering the pipeline.

                ### Consent

                **TCPA consent** is not paperwork — without it the call can be blocked outright. If the
                prospect has not agreed to be contacted, do not capture the lead.
                """,
                new() {
                    new Q("Why is copy/paste blocked on the intake form?",
                        new[] { "A browser bug", "To keep the lead live and compliant", "To slow you down", "To save storage" }, 1,
                        "Typing-only keeps the capture genuinely live and stops recycled or pre-filled data entering the pipeline."),
                    new Q("The prospect has not agreed to be contacted. What do you do?",
                        new[] { "Capture it anyway", "Do not capture the lead", "Mark it Lost later", "Ask a manager to override" }, 1,
                        "Without consent the lead should not be captured at all — the call itself may be blocked."),
                }),

            new L("dispositions-and-callbacks", "Outcomes and callbacks",
                "Recording what happened, and promising a call you will keep.", 4, "/queue",
                """
                ## Record the outcome

                Every attempt should leave a trace. From a lead's row menu you can log no answer,
                voicemail, a call back, a wrong number, or add them to **DNC**.

                ## Callbacks

                If you agree a time, schedule a **callback**. It appears on your dashboard and in
                Callbacks, so a promise you made shows up as work waiting on you rather than living in
                your memory.

                > A callback is attached to you, not to the pool. If you schedule it, it is yours.
                """,
                new() {
                    new Q("Where does a callback you schedule appear?",
                        new[] { "Nowhere until the day", "In Callbacks and on your dashboard", "In Available Leads", "Only in the lead's timeline" }, 1,
                        "Callbacks surface on your dashboard and in the Callbacks page so the promise becomes visible work."),
                }),
        }),

        // ── Verifiers ────────────────────────────────────────────────────────
        new C("verifying", "Verifying leads",
            "Check a fronted lead properly and pass it to a closer.",
            "check", AcademyLevel.Beginner, 20, new[] { Roles.Verifier }, new()
        {
            new L("the-verify-queue", "Your queue",
                "Where verification work comes from and how to take it.", 4, "/available",
                """
                ## Available Leads

                Fronted leads waiting for verification appear in **Available Leads**. If you also work
                another queue, the page splits into tabs — yours is **To verify**.

                ### Claim before you work

                Press **Claim** and the lead moves into **My Leads** and out of the shared pile. This
                is what stops two people working the same customer, so claim first and work second.

                If someone beat you to it you will be told immediately, before you have typed anything.
                """,
                new() {
                    new Q("Why claim a lead before working it?",
                        new[] { "It's faster", "So two people don't work the same customer", "To get credit", "It is not necessary" }, 1,
                        "Claiming takes the lead out of the shared pile so nobody else can start on it at the same time."),
                }),

            new L("verification-outcomes", "Outcomes",
                "Verified, not interested, DNC, callback — and what each one does.", 5, "/queue",
                """
                ## What you can record

                - **Verified** — details confirmed. The lead moves to the closer queue and is released
                  from your list.
                - **Not interested** / **DNC** — the lead is marked Lost. DNC also stops future contact.
                - **Call back** / **Busy** / **Dead air** — the lead stays with you at Fronted, and a
                  callback is scheduled so you try again.

                ### The one that hands over

                Only **Verified** moves the lead on. Everything else either ends it or keeps it with
                you, which is why marking something verified when it is not creates work for a closer
                that should never have reached them.
                """,
                new() {
                    new Q("Which outcome passes the lead to a closer?",
                        new[] { "Call back", "Busy", "Verified", "Not interested" }, 2,
                        "Verified is the hand-off. The others either end the lead or keep it with you for another attempt."),
                    new Q("You mark a lead DNC. What does that mean?",
                        new[] { "Call again tomorrow", "It is Lost and must not be contacted again", "It goes to a closer", "It stays in your queue" }, 1,
                        "DNC means do not contact. The lead is marked Lost and future contact is blocked."),
                }),
        }),

        // ── Closers ──────────────────────────────────────────────────────────
        new C("closing", "Closing sales",
            "Take a verified lead through the application to a funded policy.",
            "briefcase", AcademyLevel.Intermediate, 30, new[] { Roles.Closer, Roles.JrCloser }, new()
        {
            new L("claiming-work", "Claiming work",
                "Taking a verified lead and why ownership matters here most.", 4, "/available",
                """
                ## Available Leads → To close

                Verified leads waiting for a closer are in **Available Leads**. Claim one and it becomes
                yours.

                ### Why this matters more for closers

                A closing application asks for the customer's **banking details**. If two closers could
                work the same lead, both would collect those details and only one sale would survive.
                You now have to own a lead before the application will open at all.
                """,
                new() {
                    new Q("Why must you own a lead before opening its closing application?",
                        new[] { "For reporting", "So two closers don't both collect banking details", "To earn commission", "It's a licensing rule" }, 1,
                        "The application collects banking details. Ownership stops two people doing that for the same customer."),
                }),

            new L("the-application", "The closing application",
                "Policy details, banking, and what happens on submit.", 7, "/queue",
                """
                ## What you capture

                Carrier, plan, coverage and premium — then the customer's bank routing and account
                number for the first premium draft.

                ## The banking check

                On submit, the account is checked and a **banking code** is recorded:

                - **103 — cleared.** The sale proceeds.
                - **198 — flagged.** A verification recording must be attached before it can go on.
                - Anything else blocks submission.

                > Treat a "cleared" result as one signal, not proof. It tells you the account details
                > are well-formed; it is not a guarantee the account is good.

                ## After submit

                The lead becomes **Closed** and a sale is created. From there it goes to submission
                review, then validation, then funding.
                """,
                new() {
                    new Q("A sale comes back 198. What is required?",
                        new[] { "Nothing", "A verification recording", "A manager override", "A new lead" }, 1,
                        "198 is flagged: the sale can proceed only with a verification recording attached."),
                    new Q("What is created when you submit a closing application?",
                        new[] { "A callback", "A sale", "An invoice", "A commission payment" }, 1,
                        "Submitting creates the sale, which then moves through submission review, validation and funding."),
                }),

            new L("after-the-sale", "After the sale",
                "Submissions, validation, funding, and where commission appears.", 5, "/sales",
                """
                ## The rest of the journey

                1. **Submissions** — a submission agent reviews the sale with the carrier.
                2. **Validated** — approved, with the carrier and coverage recorded.
                3. **Funded** — the first premium has cleared.

                Commission is created as the sale progresses and appears under **My Commissions**. A
                charged-back sale reverses its commission, so the number you see is what survived.

                ### Retention

                Policies that go bad — bad bank details, NSF, cancelled, declined — surface in
                **Retention** so somebody tries to save them rather than letting them quietly lapse.
                """,
                new() {
                    new Q("Where do you see what you have earned?",
                        new[] { "Sales", "My Commissions", "Retention", "Submissions" }, 1,
                        "My Commissions shows your commission lines, including any reversals from chargebacks."),
                }),
        }),

        // ── Team leads and managers ──────────────────────────────────────────
        new C("running-a-floor", "Running a floor",
            "Supervision, the wallboard, attendance and quality.",
            "chart", AcademyLevel.Advanced, 40,
            new[] { Roles.TeamLead, Roles.CallCenterAdmin, Roles.ProgramManager, Roles.Admin, Roles.CEO }, new()
        {
            new L("supervision", "Supervisor and wallboard",
                "Seeing the floor in real time and acting on it.", 5, "/wallboard",
                """
                ## Wallboard

                A live read of the floor: who is clocked in, who is available, who is on a call, calls
                answered and abandoned, leads created and sales closed today.

                Every tile links to the rows behind the number, so "28 sales today" is one click from
                the list of those 28.

                ## Supervisor

                Per-agent live status, with the ability to change an agent's state when something has
                gone wrong — an agent stuck on a call that has ended, for example.
                """,
                new() {
                    new Q("You want to see which sales make up today's total. What do you do?",
                        new[] { "Export a report", "Click the tile", "Ask an admin", "Check the database" }, 1,
                        "Wallboard tiles link through to the underlying rows, filtered to match the number."),
                }),

            new L("attendance-and-quality", "Attendance and quality",
                "Shifts, and reviewing recorded calls.", 5, "/attendance",
                """
                ## Attendance

                Built from clock-ins. A shift left open overstates hours, which is why long shifts are
                closed automatically and marked as system-closed.

                ## QA

                **QA Reviews** scores recorded calls against a rubric; **QA Browser** is the library of
                what has been reviewed. Use it to coach rather than to catch people out — the scores
                are most useful as a pattern across an agent's calls, not as a single verdict.
                """,
                new() {
                    new Q("An agent's shift shows 60 hours. What is the most likely explanation?",
                        new[] { "They worked 60 hours", "They never clocked out", "A payroll error", "A rounding bug" }, 1,
                        "An unclosed shift keeps accruing. That is why long shifts are auto-closed and labelled."),
                }),
        }),

        // ── Administrators ───────────────────────────────────────────────────
        new C("administration", "Administering the platform",
            "Agencies, call centres, users, roles and the audit trail.",
            "shield", AcademyLevel.Admin, 50, new[] { Roles.Admin, Roles.SuperAdmin, Roles.CEO }, new()
        {
            new L("tenancy", "Agencies and call centres",
                "How the platform is partitioned, and what disabling one does.", 6, "/admin/agencies",
                """
                ## The hierarchy

                **Agency** is the tenant — a whole company. Inside it are **call centres**, the
                operational sites. Users belong to an agency, and may be pinned to one call centre.

                Pipeline data belongs to a call centre, so an agent pinned to one only sees that site's
                leads and sales.

                ## Disabling an agency

                This is the largest destructive action in the product. Disabling an agency:

                - disables **every call centre** underneath it
                - **signs out** everyone in it and blocks them from signing in
                - keeps all leads, sales and history — nothing is deleted

                Re-enabling restores exactly what the disable switched off. Anything you had disabled
                individually beforehand stays disabled.

                > The confirmation shows you the real counts before you commit, and asks you to type
                > the agency name. Read it — those numbers are people who will be signed out.
                """,
                new() {
                    new Q("Disabling an agency deletes its data.",
                        new[] { "True", "False" }, 1,
                        "Nothing is deleted. Leads, sales and history are kept; access is what is withdrawn."),
                    new Q("What happens to the agency's call centres?",
                        new[] { "Unaffected", "Disabled automatically", "Deleted", "Moved to another agency" }, 1,
                        "The cascade disables every call centre underneath, and re-enabling restores exactly those."),
                }),

            new L("users-and-roles", "Users, roles and access",
                "Creating people, placing them, and what roles actually grant.", 6, "/admin/users",
                """
                ## Creating someone

                **Add user** captures everything that decides what they can see in one step: username,
                email, roles, agency, call centre and team. A user with no call centre and no team is
                invisible to most of the app, which is why they are on the same form.

                ## Roles vs permissions

                A **role** is a job. The permissions attached to it decide what the person can do, and
                which modules appear in their sidebar.

                Some roles are **elevated** — only a SuperAdmin, Admin or CEO may grant them. Nobody
                can change their own roles, including administrators: that is what stops a user with
                user-management rights quietly promoting themselves.

                ## Status

                **Active** or **Disabled**, everywhere in the product. A third state,
                **Disabled with agency**, means the person was switched off by a cascade and will come
                back automatically when that agency is re-enabled.
                """,
                new() {
                    new Q("Can an administrator change their own roles?",
                        new[] { "Yes", "No — someone else must do it" }, 1,
                        "Self-role-changes are refused. Otherwise anyone with user-management rights could promote themselves."),
                    new Q("A user shows \"Disabled with agency\". What restores them?",
                        new[] { "Enabling the user", "Enabling their agency", "Nothing", "Re-inviting them" }, 1,
                        "They were switched off by the cascade and come back automatically when the agency is enabled."),
                }),

            new L("audit-and-security", "Audit and security",
                "What is recorded, and the controls that protect the data.", 5, "/admin/audit",
                """
                ## Audit log

                Changes are recorded with who, when, what changed and from which address. Tenant
                actions like disabling an agency record the blast radius — how many call centres and
                users were affected.

                ## What protects the data

                - **Two-factor** is mandatory for privileged roles.
                - **Sensitive fields** (identity and bank details) are encrypted at rest.
                - **Lockout** stops password guessing after repeated failures.
                - **Sessions** are short-lived, and revoking access takes effect on the next request —
                  not whenever the browser happens to reload.

                > Confidential portal credentials live under **Confidential** and every reveal is
                > audited. Assume any look is recorded, because it is.
                """,
                new() {
                    new Q("Revealing a stored portal credential is recorded.",
                        new[] { "True", "False" }, 0,
                        "Every reveal writes an audit entry naming who looked and when."),
                }),
        }),

        // ── HR ───────────────────────────────────────────────────────────────
        new C("hr", "Human resources",
            "Employees, attendance, interviews and payroll.",
            "users", AcademyLevel.Advanced, 60, new[] { Roles.HR, Roles.Admin }, new()
        {
            new L("employees-and-attendance", "Employees and attendance",
                "The employee record, and where attendance comes from.", 5, "/hr/employees",
                """
                ## The employee record

                Holds identity, contact, bank and employment details. Identity and bank fields are
                encrypted at rest and only visible to roles that need them.

                An employee can be linked to a **user account**, which is what ties their HR record to
                their activity in the CRM.

                ## Attendance

                HR attendance is the daily record — present, late, absent, leave, half day, NCNS —
                and it feeds payroll deductions directly. It can be filled from clock-in data rather
                than typed from scratch.
                """,
                new() {
                    new Q("Which attendance outcomes affect pay?",
                        new[] { "None", "Late, half day, absent and NCNS", "Only absent", "Only NCNS" }, 1,
                        "Each of those carries a deduction rule, which is why the daily record has to be accurate."),
                }),

            new L("payroll", "Payroll",
                "How a month is built, and what finalising means.", 6, "/hr/payroll",
                """
                ## How a row is built

                Basic salary, plus commission and allowances, minus deductions. Attendance-driven
                deductions are calculated from the daily wage and the rules for that call centre —
                never typed in by hand, so they cannot drift out of step with the days recorded.

                ## Draft, Auto and Finalized

                - **Auto** — a live estimate that recalculates as attendance and commission change
                - **Draft** — saved by HR, still editable and still recalculating
                - **Finalized** — locked for the month and frozen at the figures of that moment

                Finalising is the point of no return for a month: a later rate change will not
                re-price a finalised row.

                ## Filters

                A SuperAdmin sees every agency, so choose the **agency** first — several agencies have
                a call centre named "Main", and the agency is what tells them apart.
                """,
                new() {
                    new Q("What does finalising a payroll row do?",
                        new[] { "Pays it", "Locks it and freezes the figures", "Emails the employee", "Deletes the draft" }, 1,
                        "Finalised rows stop recalculating, so a later rate change cannot silently re-price a closed month."),
                }),
        }),
    };
}
