import { getErrorDetail } from "../../shared/api/apiError";
import { MESSAGES } from "../../shared/constants/messages";
import { ADMIN_MSG } from "./messages";
import { useConfirm } from "../../shared/components/ConfirmDialog";
import { useRowSelection } from "../../shared/hooks/useRowSelection";
import { exportRowsToCsv } from "../../shared/lib/csv";
import { useMemo, useState } from "react";
import {
  useListSubmissionAgentsQuery, useCreateSubmissionAgentMutation,
  useSetUserActiveMutation, useResetUserPasswordMutation, useResendInvitationMutation,
} from "../../shared/api/baseApi";
import type { SubmissionAgent } from "../../shared/api/types";
import {
  Badge, BulkActionBar, Button, Card, CardBody, CardHeader, Checkbox, EmptyState, Icon, InfoHint, Input, Modal, PageHeader,
  Pager, SearchInput, Skeleton, Table, TBody, TD, TH, THead, TR, useToast, usePagination,
} from "../../shared/ui";
import { useTableSort } from "../../shared/hooks/useTableSort";

/**
 * SuperAdmin management of central (cross-agency) Submission Agents. These are SMH-level
 * Validators — bound to no single agency — who validate and approve sales across every
 * agency and assign each approved sale to a License Agent. Mandatory 2FA applies to them.
 *
 * They aren't assigned to a call center (that's deliberate — they cover all of them). Once
 * they accept their invite they sign in and work from the **Submission Queue** in their own
 * sidebar. This page is the full lifecycle: invite, resend, reset password, deactivate.
 */
export function SubmissionAgentsPage() {
  const { data: agents, isLoading } = useListSubmissionAgentsQuery();
  const [showNew, setShowNew] = useState(false);
  const [resetting, setResetting] = useState<{ id: string; name: string } | null>(null);
  const [confirmDeactivate, setConfirmDeactivate] = useState<{ id: string; name: string } | null>(null);

  const [setActive, { isLoading: settingActive }] = useSetUserActiveMutation();
  const [resendInvite, { isLoading: resending }] = useResendInvitationMutation();
  const toast = useToast();
  const confirm = useConfirm();
  const [deactivating, setDeactivating] = useState(false);

  // Client-side search over the already-loaded agents (name / email).
  const [search, setSearch] = useState("");
  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return agents ?? [];
    return (agents ?? []).filter((a) =>
      [a.name, a.email].some((v) => (v ?? "").toLowerCase().includes(q)));
  }, [agents, search]);

  const { sorted, dirFor, toggle } = useTableSort(filtered, {
    key: "name",
    accessors: { status: (a) => (a.isActive ? "Active" : "Inactive") },
  });

  // Display-only paging over the filtered+sorted list; selection, bulk deactivate and CSV
  // export deliberately stay on the FULL filtered list, not just the visible page.
  const pg = usePagination(sorted);

  const sel = useRowSelection(sorted.map((a) => a.id));

  function exportSelected() {
    const chosen = sorted.filter((a) => sel.isSelected(a.id));
    exportRowsToCsv(chosen, [
      { header: "Name", value: (a) => a.name },
      { header: "Email", value: (a) => a.email },
      { header: "Status", value: (a) => (a.isActive ? (a.pendingInvite ? "Active — awaiting sign-in" : "Active") : "Inactive") },
      { header: "Active", value: (a) => (a.isActive ? "Yes" : "No") },
    ], `submission-agents-${new Date().toISOString().slice(0, 10)}.csv`);
    toast.success(ADMIN_MSG.common.exportReady, ADMIN_MSG.common.exportReadyDesc(chosen.length));
  }

  // Bulk deactivate loops the SAME per-row setActive mutation over the ticked agents — no new endpoint.
  async function deactivateSelected() {
    const n = sel.selectedCount;
    if (n === 0) return;
    if (!(await confirm({
      title: ADMIN_MSG.submissionAgents.deactivateConfirmTitle(n),
      description: ADMIN_MSG.submissionAgents.deactivateConfirmDesc,
      confirmLabel: ADMIN_MSG.submissionAgents.deactivateConfirmLabel,
      danger: true,
    }))) return;
    setDeactivating(true);
    try {
      await Promise.all(sel.selectedIds.map((id) => setActive({ id, isActive: false }).unwrap()));
      toast.success(ADMIN_MSG.submissionAgents.agentsDeactivated, ADMIN_MSG.common.canNoLongerSignIn(n));
      sel.clear();
    } catch (err: unknown) {
      toast.error(ADMIN_MSG.common.deactivateFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
    } finally {
      setDeactivating(false);
    }
  }

  async function reactivate(a: SubmissionAgent) {
    try {
      await setActive({ id: a.id, isActive: true }).unwrap();
      toast.success(ADMIN_MSG.submissionAgents.agentReactivated, ADMIN_MSG.submissionAgents.agentReactivatedDesc(a.name));
    } catch (err: unknown) {
      toast.error(ADMIN_MSG.common.reactivateFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
    }
  }

  async function resend(a: SubmissionAgent) {
    try {
      await resendInvite(a.id).unwrap();
      toast.success(ADMIN_MSG.common.invitationResent, ADMIN_MSG.common.invitationResentDesc(a.name));
    } catch (err: unknown) {
      toast.error(ADMIN_MSG.common.resendInviteFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
    }
  }

  return (
    <>
      <PageHeader
        title="Submission Agents"
        description="A central team that validates and approves sales across all agencies. Each is emailed an invitation and must enrol in two-factor authentication."
      />

      {/* Where they show up — answers the common "how does this agent do their job?" question. */}
      <div className="mb-4 flex items-start gap-2.5 rounded-lg border border-brand-200/70 bg-brand-50/50 px-3.5 py-3 text-sm text-ink-700">
        <Icon name="shield" size={16} className="mt-0.5 shrink-0 text-brand-600" />
        <p className="leading-relaxed">
          Submission agents are <span className="font-medium text-ink-900">cross-agency</span> — they aren't tied to a
          call center. After accepting their invite they sign in with their own account and work from the{" "}
          <span className="font-medium text-ink-900">Submission Queue</span>, where they approve sales from every agency
          and assign each to a license agent.
        </p>
      </div>

      <Card>
        <CardHeader
          title="Central submission agents"
          subtitle={agents ? `${agents.length} agent(s)` : undefined}
          action={
            <div className="flex flex-wrap items-center gap-2">
              <SearchInput value={search} onChange={setSearch}
                placeholder={ADMIN_MSG.submissionAgents.searchPlaceholder} className="w-64" />
              <Button leftIcon={<Icon name="userPlus" size={14} />} onClick={() => setShowNew(true)}>New submission agent</Button>
            </div>
          }
        />
        <CardBody>
          {isLoading ? <Skeleton className="h-32" /> : !agents || agents.length === 0 ? (
            <EmptyState icon={<Icon name="userPlus" size={20} />} title={ADMIN_MSG.submissionAgents.noAgentsTitle}
              description={ADMIN_MSG.submissionAgents.noAgentsDesc}
              action={<Button size="sm" leftIcon={<Icon name="userPlus" size={14} />} onClick={() => setShowNew(true)}>New submission agent</Button>} />
          ) : sorted.length === 0 ? (
            <EmptyState icon={<Icon name="search" size={20} />} title={ADMIN_MSG.submissionAgents.noMatchTitle}
              description={ADMIN_MSG.submissionAgents.noMatchDesc} />
          ) : (
            <>
            <Table>
              <THead>
                <TR><TH className="w-10"><Checkbox aria-label="Select all" {...sel.allCheckboxProps} /></TH><TH sortDir={dirFor("name")} onClick={() => toggle("name")}>Name</TH><TH sortDir={dirFor("email")} onClick={() => toggle("email")}>Email</TH><TH sortDir={dirFor("status")} onClick={() => toggle("status")}><span className="inline-flex items-center gap-1">Status<InfoHint title="Status" side="top">Active agents can sign in and validate sales; Inactive ones are blocked. "Awaiting sign-in" means the invite was sent but the agent hasn't signed in and set their password yet.</InfoHint></span></TH><TH className="text-right">Actions</TH></TR>
              </THead>
              <TBody>
                {pg.pageItems.map((a) => (
                  <TR key={a.id} className={sel.isSelected(a.id) ? "bg-brand-50/40" : (a.isActive ? "transition-colors hover:bg-ink-50/60" : "bg-rose-50/30")}>
                    <TD><Checkbox aria-label={`Select ${a.name}`} {...sel.checkboxProps(a.id)} /></TD>
                    <TD className={"font-medium truncate max-w-[16rem] " + (a.isActive ? "text-ink-900" : "text-ink-500 line-through decoration-rose-400/40")}>{a.name}</TD>
                    <TD className="text-sm text-ink-600">
                      <span className="inline-flex items-center gap-1.5 min-w-0">
                        <Icon name="mail" size={13} className="text-ink-400 shrink-0" />
                        <span className="truncate">{a.email}</span>
                      </span>
                    </TD>
                    <TD className="whitespace-nowrap">
                      <div className="flex flex-wrap items-center gap-1">
                        <Badge tone={a.isActive ? "success" : "neutral"} variant="soft">{a.isActive ? "Active" : "Inactive"}</Badge>
                        {a.isActive && a.pendingInvite && (
                          <Badge tone="warning" variant="soft" size="sm">
                            <Icon name="clock" size={10} className="mr-1" /> Awaiting sign-in
                          </Badge>
                        )}
                      </div>
                    </TD>
                    <TD>
                      <div className="flex items-center justify-end gap-1">
                        <Button
                          variant="ghost" size="sm" title="Reset password" aria-label="Reset password"
                          onClick={() => setResetting({ id: a.id, name: a.name })}
                        ><Icon name="key" size={15} /></Button>
                        {a.isActive && a.pendingInvite && (
                          <Button
                            variant="ghost" size="sm" disabled={resending} title="Resend invitation" aria-label="Resend invitation"
                            onClick={() => resend(a)}
                          ><Icon name="mail" size={15} /></Button>
                        )}
                        {a.isActive ? (
                          <Button
                            variant="ghost" size="sm" className="text-rose-600 hover:bg-rose-50" title="Deactivate agent" aria-label="Deactivate agent"
                            onClick={() => setConfirmDeactivate({ id: a.id, name: a.name })}
                          ><Icon name="userX" size={15} /></Button>
                        ) : (
                          <Button
                            variant="ghost" size="sm" className="text-emerald-700 hover:bg-emerald-50" title="Reactivate agent" aria-label="Reactivate agent"
                            onClick={() => reactivate(a)}
                          ><Icon name="userCheck" size={15} /></Button>
                        )}
                      </div>
                    </TD>
                  </TR>
                ))}
              </TBody>
            </Table>
            <Pager {...pg} onPage={pg.setPage} unit="agents" />
            <BulkActionBar
              count={sel.selectedCount} itemNoun="agent" onClear={sel.clear}
              actions={[
                { key: "csv", label: "Export CSV", icon: "download", onClick: exportSelected },
                { key: "deactivate", label: "Deactivate", icon: "userX", onClick: deactivateSelected, danger: true, loading: deactivating },
              ]}
            />
            </>
          )}
        </CardBody>
      </Card>

      {showNew && <NewSubmissionAgentModal onClose={() => setShowNew(false)} />}

      {resetting && <ResetPasswordModal agent={resetting} onClose={() => setResetting(null)} />}

      <Modal
        open={confirmDeactivate !== null}
        onClose={() => setConfirmDeactivate(null)}
        title="Deactivate submission agent"
        description={confirmDeactivate
          ? `${confirmDeactivate.name} will be signed out everywhere and can no longer validate or approve sales.`
          : ""}
        footer={
          <>
            <Button variant="ghost" onClick={() => setConfirmDeactivate(null)}>Cancel</Button>
            <Button
              variant="danger"
              loading={settingActive}
              onClick={async () => {
                if (!confirmDeactivate) return;
                try {
                  await setActive({ id: confirmDeactivate.id, isActive: false }).unwrap();
                  toast.success(ADMIN_MSG.submissionAgents.agentDeactivated, ADMIN_MSG.common.canNoLongerSignIn(confirmDeactivate.name));
                  setConfirmDeactivate(null);
                } catch (err: unknown) {
                  toast.error(ADMIN_MSG.common.deactivateFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
                }
              }}
            >Deactivate</Button>
          </>
        }
      >
        <div className="text-sm text-ink-700">You can reactivate this agent later if needed.</div>
      </Modal>
    </>
  );
}

function ResetPasswordModal({ agent, onClose }: { agent: { id: string; name: string }; onClose: () => void }) {
  const [resetPw, { isLoading }] = useResetUserPasswordMutation();
  const toast = useToast();
  const [newPwd, setNewPwd] = useState("");

  return (
    <Modal
      open
      onClose={onClose}
      title={`Reset password · ${agent.name}`}
      description="They'll use this password on their next sign-in and be asked to choose a new one. Any 2FA enrolment is cleared so a locked-out agent can get back in."
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>Cancel</Button>
          <Button
            loading={isLoading}
            disabled={newPwd.length < 8}
            onClick={async () => {
              try {
                await resetPw({ id: agent.id, newPassword: newPwd }).unwrap();
                toast.success(ADMIN_MSG.common.passwordReset, ADMIN_MSG.common.passwordResetDesc(agent.name));
                onClose();
              } catch (err: unknown) {
                toast.error(ADMIN_MSG.common.resetPasswordFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
              }
            }}
          >Set password</Button>
        </>
      }
    >
      <Input
        type="password"
        // "new-password" stops the browser autofilling a saved credential (and pairing it with a username field).
        autoComplete="new-password"
        name="submission-agent-new-password"
        label="New password"
        hint="Minimum 8 characters with uppercase, lowercase, digit, and symbol."
        value={newPwd}
        onChange={(e) => setNewPwd(e.target.value)}
        autoFocus
      />
    </Modal>
  );
}

function NewSubmissionAgentModal({ onClose }: { onClose: () => void }) {
  const [create, { isLoading }] = useCreateSubmissionAgentMutation();
  const toast = useToast();
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    try {
      await create({ name: name.trim(), email: email.trim() }).unwrap();
      toast.success(ADMIN_MSG.submissionAgents.agentAdded, ADMIN_MSG.common.emailedInvitation);
      onClose();
    } catch (err: unknown) {
      toast.error(ADMIN_MSG.submissionAgents.agentAddFailed, getErrorDetail(err) ?? ADMIN_MSG.common.checkFieldsAndTryAgain);
    }
  }

  return (
    <Modal open onClose={onClose} title="New submission agent" description="Creates a cross-agency Submission Agent and emails them an invitation.">
      <form onSubmit={submit} className="space-y-3">
        <Input label="Full name" required value={name} onChange={(e) => setName(e.target.value)} />
        <Input label="Email" type="email" required leftIcon={<Icon name="mail" size={14} />} value={email} onChange={(e) => setEmail(e.target.value)} />
        <div className="flex justify-end gap-2 pt-1">
          <Button type="button" variant="ghost" onClick={onClose}>Cancel</Button>
          <Button type="submit" loading={isLoading} leftIcon={<Icon name="userPlus" size={15} />}>Add + invite</Button>
        </div>
      </form>
    </Modal>
  );
}
