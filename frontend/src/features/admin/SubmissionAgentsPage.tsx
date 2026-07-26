import { getErrorDetail } from "../../shared/api/apiError";
import { useState } from "react";
import {
  useListSubmissionAgentsQuery, useCreateSubmissionAgentMutation,
  useSetUserActiveMutation, useResetUserPasswordMutation, useResendInvitationMutation,
} from "../../shared/api/baseApi";
import type { SubmissionAgent } from "../../shared/api/types";
import {
  Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, Input, Modal, PageHeader,
  Skeleton, Table, TBody, TD, TH, THead, TR, useToast,
} from "../../shared/ui";

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

  const [setActive] = useSetUserActiveMutation();
  const [resendInvite, { isLoading: resending }] = useResendInvitationMutation();
  const toast = useToast();

  async function reactivate(a: SubmissionAgent) {
    try {
      await setActive({ id: a.id, isActive: true }).unwrap();
      toast.success("Agent reactivated", `${a.name} can sign in and validate again.`);
    } catch (err: unknown) {
      toast.error("Couldn't reactivate", getErrorDetail(err) ?? "Try again.");
    }
  }

  async function resend(a: SubmissionAgent) {
    try {
      await resendInvite(a.id).unwrap();
      toast.success("Invitation resent", `A fresh temporary password was emailed to ${a.name}.`);
    } catch (err: unknown) {
      toast.error("Couldn't resend invitation", getErrorDetail(err) ?? "Try again.");
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
          action={<Button leftIcon={<Icon name="userPlus" size={14} />} onClick={() => setShowNew(true)}>New submission agent</Button>}
        />
        <CardBody>
          {isLoading ? <Skeleton className="h-32" /> : !agents || agents.length === 0 ? (
            <EmptyState icon={<Icon name="userPlus" size={20} />} title="No submission agents"
              description="Add one with the button above — they can approve sales for every agency."
              action={<Button size="sm" leftIcon={<Icon name="userPlus" size={14} />} onClick={() => setShowNew(true)}>New submission agent</Button>} />
          ) : (
            <Table>
              <THead>
                <TR><TH>Name</TH><TH>Email</TH><TH>Status</TH><TH className="text-right">Actions</TH></TR>
              </THead>
              <TBody>
                {agents.map((a) => (
                  <TR key={a.id} className={a.isActive ? "transition-colors hover:bg-ink-50/60" : "bg-rose-50/30"}>
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
              onClick={async () => {
                if (!confirmDeactivate) return;
                try {
                  await setActive({ id: confirmDeactivate.id, isActive: false }).unwrap();
                  toast.success("Agent deactivated", `${confirmDeactivate.name} can no longer sign in.`);
                  setConfirmDeactivate(null);
                } catch (err: unknown) {
                  toast.error("Couldn't deactivate", getErrorDetail(err) ?? "Try again.");
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
                toast.success("Password reset", `New password set for ${agent.name}.`);
                onClose();
              } catch (err: unknown) {
                toast.error("Couldn't reset password", getErrorDetail(err) ?? "Try again.");
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
      toast.success("Submission agent added", "They've been emailed an invitation.");
      onClose();
    } catch (err: unknown) {
      toast.error("Couldn't add submission agent", getErrorDetail(err) ?? "Check the fields and try again.");
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
