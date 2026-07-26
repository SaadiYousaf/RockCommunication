import { Badge, Card, CardBody, CardHeader, Icon, InfoHint, PageHeader } from "../../shared/ui";
import type { BadgeTone } from "../../shared/ui";
import type { IconName } from "../../shared/ui/Icon";

/**
 * In-app onboarding guide: the pipeline at a glance, one real lead's end-to-end journey,
 * every role's responsibility, and a plain-language glossary. Visible to every authenticated
 * user (no module/role gate) so anyone can learn how the CRM fits together.
 */

// ── Pipeline stages, in order ────────────────────────────────────────────────
const STAGES: { label: string; tone: BadgeTone }[] = [
  { label: "New", tone: "neutral" },
  { label: "Fronted", tone: "info" },
  { label: "Verified", tone: "brand" },
  { label: "Closed", tone: "accent" },
  { label: "Validated", tone: "warning" },
  { label: "Funded", tone: "success" },
];

// ── Maria's journey: one real lead from click to commission ───────────────────
interface Step {
  n: number;
  stage: string;
  stageTone: BadgeTone;
  role: string;
  roleTone: BadgeTone;
  icon: IconName;
  title: string;
  what: string;
  system: string;
}
const JOURNEY: Step[] = [
  {
    n: 1, stage: "New", stageTone: "neutral", role: "System / Campaign", roleTone: "neutral", icon: "inbox",
    title: "The lead arrives",
    what: "Maria Lopez, 58, in Phoenix AZ clicks a Facebook ad for final-expense life insurance. Her details land in the CRM through a lead list import (or a web form).",
    system: "Creates the lead, scores it, and checks it against the DNC list before anyone dials.",
  },
  {
    n: 2, stage: "Fronted", stageTone: "info", role: "Fronter", roleTone: "info", icon: "phone",
    title: "First contact",
    what: "A Fronter calls Maria, confirms she's genuinely interested and it's a good time to talk, and captures her consent to be contacted (Jornaya).",
    system: "Moves the lead to Fronted and drops it into the Verifier's queue.",
  },
  {
    n: 3, stage: "Verified", stageTone: "brand", role: "Verifier", roleTone: "brand", icon: "userCheck",
    title: "Confirm the details",
    what: "The Verifier re-confirms Maria's identity and basic eligibility (age, health questions) and verifies the Jornaya consent token is valid.",
    system: "Moves the lead to Verified and re-scores it, then routes it to the Closer's queue.",
  },
  {
    n: 4, stage: "Closed", stageTone: "accent", role: "Closer", roleTone: "accent", icon: "briefcase",
    title: "Close the sale",
    what: "The Closer presents a plan Maria can afford ($40/mo whole-life, $10k coverage), handles her questions, and closes. She gives her bank routing + account for the monthly draft. (A Jr Closer may assist and split the credit.)",
    system: "Moves the lead to Closed — ready to record as a sale.",
  },
  {
    n: 5, stage: "Closed", stageTone: "accent", role: "Closer", roleTone: "accent", icon: "dollar",
    title: "Record the sale",
    what: "The Closer records the sale. Lyons validates Maria's bank account and returns a banking code. If it's flagged (code 198), the closer must add a reason or attach a verification recording to proceed.",
    system: "Creates the Sale with a per-agency serial number and drafts each commission line as unpaid.",
  },
  {
    n: 6, stage: "Validated", stageTone: "warning", role: "Submission Agent", roleTone: "warning", icon: "shield",
    title: "Submit & approve",
    what: "A Submission Agent copies Maria's details into the carrier's portal, submits the policy, records the approved outcome, and assigns the License Agent of record.",
    system: "Marks the sale Approved / Validated and creates the license-agent approval commission.",
  },
  {
    n: 7, stage: "Validated", stageTone: "warning", role: "License Agent", roleTone: "danger", icon: "user",
    title: "The agent of record",
    what: "The License Agent is the licensed person whose name is on Maria's policy with the carrier. They review the sale assigned to them and earn the approval commission.",
    system: "The sale now shows in that agent's Sales and Commissions.",
  },
  {
    n: 8, stage: "Funded", stageTone: "success", role: "Carrier / Fund", roleTone: "success", icon: "check",
    title: "The policy funds",
    what: "The carrier funds the policy once Maria's first monthly draft clears the bank.",
    system: "Moves the sale to Funded — every commission on it becomes payable.",
  },
  {
    n: 9, stage: "Funded", stageTone: "success", role: "Manager / Payroll", roleTone: "brand", icon: "doc",
    title: "Everyone gets paid",
    what: "A manager runs payroll for the period. The fronter, closer, submission agent and license agent all see their share of Maria's sale marked Paid.",
    system: "Freezes that period's commissions and marks them Paid.",
  },
];

// ── What happens when it doesn't go straight through ──────────────────────────
const BRANCHES: { label: string; tone: BadgeTone; text: string }[] = [
  { label: "Call me later", tone: "info", text: "The lead gets a Callback scheduled and comes back to the queue at the right time." },
  { label: "Bad Bank / NSF", tone: "danger", text: "The bank account is invalid or the draft bounces — the sale is reworked or marked lost." },
  { label: "Declined", tone: "danger", text: "The carrier declines the application; the sale is closed out as lost." },
  { label: "Winback", tone: "warning", text: "A lapsed or lost customer is re-worked later through a winback cadence." },
];

// ── Roles & responsibilities ──────────────────────────────────────────────────
const ROLES: { name: string; tone: BadgeTone; icon: IconName; blurb: string }[] = [
  { name: "Fronter", tone: "info", icon: "phone", blurb: "Makes first contact, confirms interest, captures consent, and hands warm leads to verification." },
  { name: "Verifier", tone: "brand", icon: "userCheck", blurb: "Confirms identity, eligibility and consent so only clean leads reach a closer." },
  { name: "Closer", tone: "accent", icon: "briefcase", blurb: "Presents the plan, closes the sale, collects banking, and records it." },
  { name: "Jr Closer", tone: "accent", icon: "users", blurb: "Assists on a close and shares a portion of the closing credit." },
  { name: "Submission Agent", tone: "warning", icon: "shield", blurb: "Central team that submits sales to carriers, sets the approval outcome, and assigns the license agent — across all agencies." },
  { name: "License Agent", tone: "danger", icon: "user", blurb: "The licensed agent of record on the policy; earns the approval commission for sales assigned to them." },
  { name: "Team Lead", tone: "brand", icon: "users", blurb: "Runs a team, sees their team's pipeline, and can earn an override on team sales." },
  { name: "Program / Project Manager", tone: "brand", icon: "chart", blurb: "Oversees campaigns and the wider floor; broad read access and reporting." },
  { name: "Call Center Admin", tone: "brand", icon: "building", blurb: "Manages the users and settings of a single call center." },
  { name: "QA Manager", tone: "accent", icon: "star", blurb: "Reviews calls against rubrics and tracks agent scorecards." },
  { name: "CEO", tone: "success", icon: "building", blurb: "Agency owner — full visibility of their agency's pipeline, sales and payroll." },
  { name: "Admin", tone: "success", icon: "cog", blurb: "Administers one agency: users, roles, call centers and configuration." },
  { name: "Super Admin", tone: "danger", icon: "shield", blurb: "Platform owner — manages every agency and the central submission team; the only cross-tenant role." },
];

// ── Glossary ──────────────────────────────────────────────────────────────────
const GLOSSARY: { term: string; def: string }[] = [
  { term: "Jornaya", def: "A consent-tracking token proving the lead agreed to be contacted. Verified before a sale can proceed." },
  { term: "Lyons", def: "The bank-account validation service. It checks the routing/account number and returns a banking code." },
  { term: "Banking code 198", def: "A flag meaning the account is usable but risky — the closer must add a reason or a verification recording to submit." },
  { term: "Commission rules", def: "Named payout rules — e.g. closer flat rate, validator bonus, license-agent approval — configurable per agency." },
  { term: "Payroll run", def: "Locks a period's commissions and marks them Paid. Once paid, a line can't be changed." },
  { term: "Cadence", def: "An automated sequence of follow-up touches (calls, SMS, email) that works a lead over time." },
  { term: "DNC", def: "Do-Not-Call list. The dialer blocks flagged numbers automatically; always honor customer opt-outs." },
  { term: "Wrap-up", def: "The disposition an agent sets after every call (Sale, Callback, Not Interested…) before going Available again." },
];

function Arrow() {
  return <Icon name="arrowRight" size={14} className="text-ink-300 shrink-0" />;
}

export function GuidePage() {
  return (
    <>
      <PageHeader
        title="How Rock CRM works"
        description="A guided tour of the pipeline, every role, and one real lead's journey from first click to paid commission."
      />

      {/* Pipeline at a glance */}
      <Card className="mb-6">
        <CardHeader
          title={
            <span className="inline-flex items-center gap-1.5">
              The pipeline at a glance
              <InfoHint title="Stages" side="bottom">
                Every lead moves left-to-right through these stages. Some branch off to Callback, Winback or Lost along the way.
              </InfoHint>
            </span>
          }
          subtitle="A lead flows through these stages, each owned by a different role."
        />
        <CardBody className="pt-0">
          <div className="flex flex-wrap items-center gap-2">
            {STAGES.map((s, i) => (
              <span key={s.label} className="inline-flex items-center gap-2">
                <Badge tone={s.tone} variant="soft">{s.label}</Badge>
                {i < STAGES.length - 1 && <Arrow />}
              </span>
            ))}
          </div>
        </CardBody>
      </Card>

      {/* Maria's journey */}
      <Card className="mb-6">
        <CardHeader
          title="Meet Maria — one lead, end to end"
          subtitle="Maria Lopez, 58, from Phoenix. Here's exactly what happens, and who does it, from her first click to everyone getting paid."
        />
        <CardBody className="pt-0">
          <ol className="space-y-3">
            {JOURNEY.map((step) => (
              <li key={step.n} className="flex gap-4 rounded-xl border border-ink-100 bg-white p-4">
                <div className="shrink-0">
                  <div className="h-10 w-10 rounded-full bg-brand-50 text-brand-600 grid place-items-center">
                    <Icon name={step.icon} size={18} />
                  </div>
                </div>
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-2 mb-1">
                    <span className="text-[11px] font-bold text-ink-400 tabular-nums">STEP {step.n}</span>
                    <span className="font-semibold text-ink-900">{step.title}</span>
                    <Badge tone={step.stageTone} variant="soft" size="sm">{step.stage}</Badge>
                    <Badge tone={step.roleTone} variant="soft" size="sm">
                      <Icon name={step.icon} size={10} className="mr-1" />{step.role}
                    </Badge>
                  </div>
                  <p className="text-sm text-ink-700 leading-relaxed">{step.what}</p>
                  <p className="text-xs text-ink-500 mt-1.5 inline-flex items-start gap-1.5">
                    <Icon name="cog" size={12} className="mt-0.5 shrink-0 text-ink-400" />
                    <span><span className="font-medium text-ink-600">What the system does:</span> {step.system}</span>
                  </p>
                </div>
              </li>
            ))}
          </ol>

          {/* Branches */}
          <div className="mt-5 rounded-xl bg-ink-50/60 p-4">
            <div className="text-xs font-semibold uppercase tracking-[0.08em] text-ink-500 mb-2.5 inline-flex items-center gap-1.5">
              <Icon name="filter" size={13} /> When it doesn't go straight through
            </div>
            <div className="grid gap-2.5 sm:grid-cols-2">
              {BRANCHES.map((b) => (
                <div key={b.label} className="flex items-start gap-2.5">
                  <Badge tone={b.tone} variant="soft" size="sm">{b.label}</Badge>
                  <span className="text-sm text-ink-600 leading-snug">{b.text}</span>
                </div>
              ))}
            </div>
          </div>
        </CardBody>
      </Card>

      {/* Roles & responsibilities */}
      <Card className="mb-6">
        <CardHeader
          title="Roles & responsibilities"
          subtitle="Who does what. Each role only sees the parts of the CRM they need."
        />
        <CardBody className="pt-0">
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {ROLES.map((r) => (
              <div key={r.name} className="rounded-xl border border-ink-100 p-3.5">
                <div className="flex items-center gap-2 mb-1.5">
                  <div className="h-7 w-7 rounded-lg bg-ink-50 text-ink-500 grid place-items-center shrink-0">
                    <Icon name={r.icon} size={14} />
                  </div>
                  <Badge tone={r.tone} variant="soft">{r.name}</Badge>
                </div>
                <p className="text-sm text-ink-600 leading-snug">{r.blurb}</p>
              </div>
            ))}
          </div>
        </CardBody>
      </Card>

      {/* Glossary */}
      <Card className="mb-6">
        <CardHeader title="Glossary" subtitle="Plain-language definitions of the terms you'll see around the app." />
        <CardBody className="pt-0">
          <dl className="grid gap-x-8 gap-y-3 sm:grid-cols-2">
            {GLOSSARY.map((g) => (
              <div key={g.term}>
                <dt className="text-sm font-semibold text-ink-900">{g.term}</dt>
                <dd className="text-sm text-ink-600 leading-snug">{g.def}</dd>
              </div>
            ))}
          </dl>
        </CardBody>
      </Card>
    </>
  );
}
