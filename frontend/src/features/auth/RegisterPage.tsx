import { getErrorDetail } from "../../shared/api/apiError";
import { useMemo, useState } from "react";
import { useSelector } from "react-redux";
import { useNavigate } from "react-router-dom";
import { useListRolesQuery, useRegisterMutation } from "../../shared/api/baseApi";
import {
  Badge, Button, Card, CardBody, CardHeader, Icon, Input, PageHeader,
  Skeleton, useToast, cn,
} from "../../shared/ui";
import type { RootState } from "../../app/store";

/**
 * Role buckets — keeps the role picker scannable instead of a wall of checkboxes.
 * Anything not in here falls into "Other".
 */
const ROLE_GROUPS: { label: string; tone: "danger" | "warning" | "info" | "brand" | "neutral" | "success"; roles: string[] }[] = [
  { label: "Leadership",   tone: "danger",  roles: ["Admin", "ProgramManager", "TeamLead"] },
  { label: "Front line",   tone: "info",    roles: ["Fronter", "Verifier"] },
  { label: "Closing",      tone: "success", roles: ["JrCloser", "Closer"] },
  { label: "Validation",   tone: "warning", roles: ["Validator", "SelfValidator"] },
  { label: "Engagement",   tone: "neutral", roles: ["Followups", "Correspondence", "Winbacks"] },
];

const ROLE_DESCRIPTIONS: Record<string, string> = {
  Admin: "Full system access — user/role management and integrations.",
  ProgramManager: "Same as Admin minus the system-level toggles.",
  TeamLead: "Floor supervision: live agents, QA, KPIs, scripts.",
  Fronter: "Front-line outbound calling and qualification.",
  Verifier: "Verifies fronted leads before routing to a closer.",
  JrCloser: "Junior closer — closes warmer leads.",
  Closer: "Closes deals and records sales.",
  Validator: "Validates submitted sales for QA/funding.",
  SelfValidator: "Closer who can self-validate their own sales.",
  Followups: "Works follow-up cadences.",
  Correspondence: "Handles email/SMS-only outreach.",
  Winbacks: "Re-engages lost / churned leads.",
};

/** Simple password-strength heuristic — score 0..4. */
function scorePassword(pw: string): { score: 0 | 1 | 2 | 3 | 4; label: string; tone: "danger" | "warning" | "neutral" | "success" } {
  if (!pw) return { score: 0, label: "Empty", tone: "neutral" };
  let s = 0;
  if (pw.length >= 8) s++;
  if (pw.length >= 12) s++;
  if (/[A-Z]/.test(pw) && /[a-z]/.test(pw)) s++;
  if (/\d/.test(pw)) s++;
  if (/[^A-Za-z0-9]/.test(pw)) s++;
  s = Math.min(4, s) as 0 | 1 | 2 | 3 | 4;
  const map = ["Very weak", "Weak", "Fair", "Good", "Strong"] as const;
  const tone = (["danger", "danger", "warning", "warning", "success"] as const)[s];
  return { score: s as 0 | 1 | 2 | 3 | 4, label: map[s], tone };
}

function generateStrongPassword(): string {
  const lower = "abcdefghjkmnpqrstuvwxyz";
  const upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
  const digits = "23456789";
  const symbols = "!@#$%^&*?";
  const all = lower + upper + digits + symbols;
  let pw =
    upper[Math.floor(Math.random() * upper.length)] +
    lower[Math.floor(Math.random() * lower.length)] +
    digits[Math.floor(Math.random() * digits.length)] +
    symbols[Math.floor(Math.random() * symbols.length)];
  for (let i = 0; i < 10; i++) pw += all[Math.floor(Math.random() * all.length)];
  return pw.split("").sort(() => Math.random() - 0.5).join("");
}

export function RegisterPage() {
  const auth = useSelector((s: RootState) => s.auth);
  const { data: roles, isLoading: loadingRoles } = useListRolesQuery();
  const [register, { isLoading }] = useRegisterMutation();
  const toast = useToast();
  const navigate = useNavigate();

  const [email, setEmail] = useState("");
  const [userName, setUserName] = useState("");
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [selectedRoles, setSelectedRoles] = useState<string[]>([]);
  const [showPwd, setShowPwd] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const agencyId = auth.user?.agencyId ?? "";
  const strength = scorePassword(password);
  const passwordMatches = password.length > 0 && password === confirm;
  const passwordMismatch = confirm.length > 0 && password !== confirm;

  const grouped = useMemo(() => {
    const lookup = new Map((roles ?? []).map((r) => [r.name, r]));
    const seen = new Set<string>();
    const groups = ROLE_GROUPS.map((g) => {
      const items = g.roles.filter((rn) => lookup.has(rn));
      items.forEach((rn) => seen.add(rn));
      return { ...g, items };
    }).filter((g) => g.items.length > 0);
    const others = (roles ?? []).filter((r) => !seen.has(r.name)).map((r) => r.name);
    if (others.length > 0) {
      groups.push({ label: "Other", tone: "neutral", roles: others, items: others });
    }
    return groups;
  }, [roles]);

  function toggleRole(name: string) {
    setSelectedRoles((cur) =>
      cur.includes(name) ? cur.filter((r) => r !== name) : [...cur, name]
    );
  }

  function fillDemo() {
    const tag = Math.random().toString(36).slice(2, 6);
    setUserName(`agent.${tag}`);
    setEmail(`agent.${tag}@apexcrm.local`);
    const pw = generateStrongPassword();
    setPassword(pw); setConfirm(pw);
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    // Password is optional — leaving it empty tells the backend to generate a
    // temporary password, email it, and force the new user to change it on first login.
    const usingTempPassword = !password.trim();
    if (!usingTempPassword && password !== confirm) {
      setError("Passwords do not match.");
      return;
    }
    if (selectedRoles.length === 0) {
      setError("Pick at least one role.");
      return;
    }
    try {
      const created = await register({
        email,
        userName,
        password: usingTempPassword ? null : password,
        agencyId,
        roles: selectedRoles,
      }).unwrap();
      if (created.mustChangePassword) {
        toast.success(
          "Invite sent",
          `${userName} will receive a temporary password by email and be prompted to change it on first sign-in.`,
        );
      } else {
        toast.success("User created", `${userName} can now sign in.`);
      }
      navigate("/admin/users");
    } catch (err: unknown) {
      const msg = getErrorDetail(err) ?? getErrorDetail(err) ?? "Registration failed.";
      setError(msg);
      toast.error("Registration failed", msg);
    }
  }

  return (
    <>
      <PageHeader
        title="Register a new user"
        description="Create an account, assign starting roles, and grant module access through Role Management."
        breadcrumbs={[{ label: "Admin" }, { label: "Register user" }]}
        actions={
          <Button variant="outline" onClick={fillDemo}>
            Fill demo data
          </Button>
        }
      />

      <form onSubmit={handleSubmit} className="grid grid-cols-1 lg:grid-cols-3 gap-5">
        {/* Main form */}
        <div className="lg:col-span-2 space-y-5">
          <Card>
            <CardHeader title="Account" subtitle="The credentials this user will sign in with." />
            <CardBody className="pt-0 space-y-4">
              <div className="grid sm:grid-cols-2 gap-4">
                <Input
                  label="Username"
                  placeholder="e.g. james.kowalski"
                  value={userName}
                  onChange={(e) => setUserName(e.target.value)}
                  leftIcon={<Icon name="users" size={16} />}
                  hint="Letters, numbers, dots — used for sign-in."
                  required autoFocus autoComplete="off"
                />
                <Input
                  label="Email"
                  type="email"
                  placeholder="user@company.com"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  leftIcon={<Icon name="chat" size={16} />}
                  hint="Used for password resets and 2FA."
                  required autoComplete="off"
                />
              </div>

              <div className="grid sm:grid-cols-2 gap-4">
                <div>
                  <Input
                    label="Password"
                    type={showPwd ? "text" : "password"}
                    placeholder="Leave blank to email a temporary one"
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    leftIcon={<Icon name="shield" size={16} />}
                    hint={password.trim() ? "User will sign in with this password." : "We'll email a temporary password and force a change on first login."}
                    rightSlot={
                      <button
                        type="button"
                        onClick={() => setShowPwd((s) => !s)}
                        className="text-xs text-ink-500 hover:text-ink-800 px-2"
                      >{showPwd ? "Hide" : "Show"}</button>
                    }
                    autoComplete="new-password"
                  />
                  {password.length > 0 && (
                    <PasswordStrengthBar strength={strength} hasInput={password.length > 0} />
                  )}
                </div>

                <Input
                  label="Confirm password"
                  type={showPwd ? "text" : "password"}
                  placeholder={password.trim() ? "Retype the password" : "Not needed"}
                  value={confirm}
                  onChange={(e) => setConfirm(e.target.value)}
                  leftIcon={<Icon name="shield" size={16} />}
                  rightSlot={
                    passwordMatches
                      ? <span className="text-emerald-600 px-2"><Icon name="check" size={14} /></span>
                      : passwordMismatch
                      ? <span className="text-rose-600 px-2"><Icon name="x" size={14} /></span>
                      : undefined
                  }
                  error={passwordMismatch ? "Passwords don't match" : undefined}
                  disabled={!password.trim()}
                  autoComplete="new-password"
                />
              </div>

              <Button
                type="button" variant="ghost" size="sm"
                leftIcon={<Icon name="shield" size={14} />}
                onClick={() => {
                  const pw = generateStrongPassword();
                  setPassword(pw); setConfirm(pw); setShowPwd(true);
                }}
              >
                Generate a strong password
              </Button>
            </CardBody>
          </Card>

          <Card>
            <CardHeader
              title="Roles"
              subtitle="Roles drive what menu items and pages this user sees. Pick at least one."
              action={
                selectedRoles.length > 0 && (
                  <Badge tone="brand" variant="soft">
                    {selectedRoles.length} selected
                  </Badge>
                )
              }
            />
            <CardBody className="pt-0 space-y-5">
              {loadingRoles ? (
                <div className="space-y-2">
                  {[0, 1, 2].map((i) => <Skeleton key={i} className="h-10" />)}
                </div>
              ) : (
                grouped.map((g) => (
                  <div key={g.label}>
                    <div className="flex items-center gap-2 mb-2">
                      <Badge tone={g.tone} variant="soft" dot>{g.label}</Badge>
                      <div className="flex-1 h-px bg-ink-100" />
                    </div>
                    <div className="grid sm:grid-cols-2 lg:grid-cols-3 gap-2">
                      {g.items.map((roleName) => {
                        const active = selectedRoles.includes(roleName);
                        return (
                          <button
                            key={roleName}
                            type="button"
                            onClick={() => toggleRole(roleName)}
                            className={cn(
                              "flex items-start gap-2.5 px-3 py-2.5 rounded-lg border text-left transition-colors",
                              active
                                ? "border-brand-500 bg-brand-50 ring-1 ring-brand-200"
                                : "border-ink-200 hover:border-ink-300 hover:bg-ink-50",
                            )}
                          >
                            <span className={cn(
                              "mt-0.5 h-4 w-4 rounded border grid place-items-center shrink-0 transition-colors",
                              active
                                ? "border-brand-600 bg-brand-600 text-white"
                                : "border-ink-300 bg-white",
                            )}>
                              {active && <Icon name="check" size={12} />}
                            </span>
                            <span className="min-w-0">
                              <div className={cn("text-sm font-medium", active ? "text-brand-700" : "text-ink-800")}>
                                {roleName}
                              </div>
                              {ROLE_DESCRIPTIONS[roleName] && (
                                <div className="text-[11px] text-ink-500 leading-snug mt-0.5 line-clamp-2">
                                  {ROLE_DESCRIPTIONS[roleName]}
                                </div>
                              )}
                            </span>
                          </button>
                        );
                      })}
                    </div>
                  </div>
                ))
              )}
            </CardBody>
          </Card>

          {error && !passwordMismatch && (
            <Card className="border-rose-300 bg-rose-50/40">
              <CardBody className="flex items-start gap-3">
                <div className="h-9 w-9 rounded-lg bg-rose-100 text-rose-600 grid place-items-center shrink-0">
                  <Icon name="x" size={16} />
                </div>
                <div className="text-sm text-rose-800">{error}</div>
              </CardBody>
            </Card>
          )}

          <div className="flex flex-wrap gap-2 pt-1">
            <Button
              type="submit" size="lg" loading={isLoading}
              leftIcon={<Icon name="plus" size={16} />}
              disabled={!userName || !email || !password || !passwordMatches || selectedRoles.length === 0}
            >
              Create user
            </Button>
            <Button type="button" variant="ghost" size="lg" onClick={() => navigate(-1)}>
              Cancel
            </Button>
          </div>
        </div>

        {/* Side preview / tips */}
        <div className="space-y-5">
          <PreviewCard
            userName={userName}
            email={email}
            selectedRoles={selectedRoles}
          />

          <Card>
            <CardHeader title="Tips" />
            <CardBody className="pt-0 space-y-3 text-sm text-ink-700">
              <Tip icon="shield" text="Passwords need 8+ characters with upper, lower, digit and symbol." />
              <Tip icon="users" text="Pick the smallest role that lets the user do their job." />
              <Tip icon="chat" text="The user gets a welcome email with a sign-in link if email is configured." />
              <Tip icon="check" text="You can change roles later from User Management." />
            </CardBody>
          </Card>
        </div>
      </form>
    </>
  );
}

function PasswordStrengthBar({ strength, hasInput }: { strength: ReturnType<typeof scorePassword>; hasInput: boolean }) {
  const colors = ["bg-rose-500", "bg-rose-500", "bg-amber-500", "bg-amber-500", "bg-emerald-500"];
  const widths = ["w-[10%]", "w-1/4", "w-1/2", "w-3/4", "w-full"];
  const textTone = strength.tone === "danger" ? "text-rose-600"
    : strength.tone === "warning" ? "text-amber-700"
    : strength.tone === "success" ? "text-emerald-700" : "text-ink-500";
  return (
    <div className="mt-1.5">
      <div className="h-1 bg-ink-100 rounded-full overflow-hidden">
        <div className={cn(
          "h-full rounded-full transition-all",
          hasInput ? colors[strength.score] : "bg-transparent",
          hasInput ? widths[strength.score] : "w-0",
        )} />
      </div>
      <div className={cn("text-[11px] mt-1 font-medium", textTone)}>
        {hasInput ? `Password strength: ${strength.label}` : "Type a password to see strength."}
      </div>
    </div>
  );
}

function PreviewCard({
  userName, email, selectedRoles,
}: { userName: string; email: string; selectedRoles: string[] }) {
  const initials = (userName || "?").split(/[._\s]/).filter(Boolean).slice(0, 2).map((p) => p[0]?.toUpperCase()).join("") || "?";

  return (
    <Card elevated className="overflow-hidden">
      <div className="bg-gradient-to-br from-brand-600 via-brand-700 to-ink-950 text-white p-5">
        <div className="text-[10px] font-semibold uppercase tracking-[0.16em] text-white/70 mb-3">
          Preview
        </div>
        <div className="flex items-center gap-3">
          <div className="h-12 w-12 rounded-xl bg-white/15 ring-1 ring-white/20 backdrop-blur grid place-items-center text-lg font-semibold">
            {initials}
          </div>
          <div className="min-w-0">
            <div className="font-semibold truncate">{userName || "Username"}</div>
            <div className="text-xs text-white/70 truncate">{email || "email@company.com"}</div>
          </div>
        </div>
      </div>
      <CardBody>
        <div className="text-xs font-semibold text-ink-700 uppercase tracking-wider mb-2">Roles</div>
        {selectedRoles.length === 0 ? (
          <div className="text-sm text-ink-500">No roles selected yet.</div>
        ) : (
          <div className="flex flex-wrap gap-1.5">
            {selectedRoles.map((r) => (
              <Badge key={r} tone="brand" variant="soft">{r}</Badge>
            ))}
          </div>
        )}
      </CardBody>
    </Card>
  );
}

function Tip({ icon, text }: { icon: any; text: string }) {
  return (
    <div className="flex items-start gap-2.5">
      <div className="h-7 w-7 rounded-md bg-brand-50 text-brand-600 grid place-items-center shrink-0">
        <Icon name={icon} size={14} />
      </div>
      <div className="leading-snug">{text}</div>
    </div>
  );
}
