import { getErrorDetail } from "../../shared/api/apiError";
import { useState } from "react";
import {
  useListSubmissionAgentsQuery, useCreateSubmissionAgentMutation,
} from "../../shared/api/baseApi";
import {
  Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, Input, Modal, PageHeader,
  Skeleton, Table, TBody, TD, TH, THead, TR, useToast,
} from "../../shared/ui";

/**
 * SuperAdmin management of central (cross-agency) Submission Agents. These are SMH-level
 * Validators — bound to no single agency — who validate and approve sales across every
 * agency and assign each approved sale to a License Agent. Mandatory 2FA applies to them.
 */
export function SubmissionAgentsPage() {
  const { data: agents, isLoading } = useListSubmissionAgentsQuery();
  const [showNew, setShowNew] = useState(false);

  return (
    <>
      <PageHeader
        title="Submission Agents"
        description="A central team that validates and approves sales across all agencies. Each is emailed an invitation and must enrol in two-factor authentication."
      />
      <Card>
        <CardHeader
          title="Central submission agents"
          subtitle={agents ? `${agents.length} agent(s)` : undefined}
          action={<Button leftIcon={<Icon name="userPlus" size={14} />} onClick={() => setShowNew(true)}>New submission agent</Button>}
        />
        <CardBody>
          {isLoading ? <Skeleton className="h-32" /> : !agents || agents.length === 0 ? (
            <EmptyState icon={<Icon name="userPlus" size={20} />} title="No submission agents"
              description="Add one with the button above — they can approve sales for every agency." />
          ) : (
            <Table>
              <THead>
                <TR><TH>Name</TH><TH>Email</TH><TH>Status</TH></TR>
              </THead>
              <TBody>
                {agents.map((a) => (
                  <TR key={a.id}>
                    <TD className="font-medium text-ink-900">{a.name}</TD>
                    <TD className="text-sm text-ink-600">{a.email}</TD>
                    <TD><Badge tone={a.isActive ? "success" : "neutral"} variant="soft">{a.isActive ? "Active" : "Inactive"}</Badge></TD>
                  </TR>
                ))}
              </TBody>
            </Table>
          )}
        </CardBody>
      </Card>

      {showNew && <NewSubmissionAgentModal onClose={() => setShowNew(false)} />}
    </>
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
