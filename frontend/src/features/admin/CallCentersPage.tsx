import { useState } from "react";
import {
  useListCallCentersQuery, useCreateCallCenterMutation, useUpdateCallCenterMutation,
} from "../../shared/api/baseApi";
import type { CallCenterDto } from "../../shared/api/types";
import {
  Badge, Button, Card, CardBody, CardHeader, EmptyState, Icon, Input, Modal, PageHeader,
  Skeleton, Table, TBody, TD, TH, THead, TR, useToast,
} from "../../shared/ui";

/**
 * Manage the call centers within the current agency. Call centers are the finer
 * data-isolation unit: pipeline data belongs to one, and call-center-scoped agents
 * only see their own. Agency-level admins/managers see and manage all of them.
 */
export function CallCentersPage() {
  const { data: list, isLoading } = useListCallCentersQuery();
  const [createCc, { isLoading: creating }] = useCreateCallCenterMutation();
  const [updateCc, { isLoading: saving }] = useUpdateCallCenterMutation();
  const toast = useToast();

  const [editing, setEditing] = useState<CallCenterDto | null>(null);
  const [showNew, setShowNew] = useState(false);
  const [form, setForm] = useState({ name: "", code: "" });

  async function submitNew(e: React.FormEvent) {
    e.preventDefault();
    try {
      await createCc({ name: form.name.trim(), code: form.code.trim() || null }).unwrap();
      toast.success("Call center created", form.name);
      setShowNew(false); setForm({ name: "", code: "" });
    } catch (err: any) {
      toast.error("Couldn't create", err?.data?.detail ?? "Check the name and try again.");
    }
  }

  async function submitEdit(e: React.FormEvent) {
    e.preventDefault();
    if (!editing) return;
    try {
      await updateCc({
        id: editing.id, name: editing.name.trim(), code: editing.code?.trim() || null,
        isActive: editing.isActive,
      }).unwrap();
      toast.success("Saved", editing.name);
      setEditing(null);
    } catch (err: any) {
      toast.error("Couldn't save", err?.data?.detail ?? "Try again.");
    }
  }

  return (
    <>
      <PageHeader
        title="Call Centers"
        description="Operational units within your agency. Assign agents to a call center to isolate their pipeline data."
        actions={
          <Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setShowNew(true)}>
            Add call center
          </Button>
        }
      />
      <Card>
        <CardHeader title="Call centers" subtitle={list ? `${list.length} total` : undefined} />
        <CardBody>
          {isLoading ? <Skeleton className="h-40" /> : !list || list.length === 0 ? (
            <EmptyState icon={<Icon name="building" size={20} />} title="No call centers yet"
              description="Create one, then assign agents to it from User Management." />
          ) : (
            <Table>
              <THead>
                <TR><TH>Name</TH><TH>Code</TH><TH>Leads</TH><TH>Status</TH><TH></TH></TR>
              </THead>
              <TBody>
                {list.map((c) => (
                  <TR key={c.id}>
                    <TD className="font-medium text-ink-900">{c.name}</TD>
                    <TD className="font-mono text-xs text-ink-600">{c.code || "—"}</TD>
                    <TD className="text-sm">{c.leadCount}</TD>
                    <TD>{c.isActive
                      ? <Badge tone="success" variant="soft">Active</Badge>
                      : <Badge tone="neutral" variant="soft">Inactive</Badge>}</TD>
                    <TD className="text-right">
                      <Button size="sm" variant="outline" onClick={() => setEditing({ ...c })}>Edit</Button>
                    </TD>
                  </TR>
                ))}
              </TBody>
            </Table>
          )}
        </CardBody>
      </Card>

      <Modal open={showNew} onClose={() => setShowNew(false)} title="New call center">
        <form onSubmit={submitNew} className="space-y-3">
          <Input label="Name" required value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
          <Input label="Code" placeholder="Optional short code" value={form.code} onChange={(e) => setForm({ ...form, code: e.target.value })} />
          <div className="flex justify-end gap-2 pt-1">
            <Button type="button" variant="outline" onClick={() => setShowNew(false)}>Cancel</Button>
            <Button type="submit" loading={creating}>Create</Button>
          </div>
        </form>
      </Modal>

      <Modal open={!!editing} onClose={() => setEditing(null)} title="Edit call center">
        {editing && (
          <form onSubmit={submitEdit} className="space-y-3">
            <Input label="Name" required value={editing.name} onChange={(e) => setEditing({ ...editing, name: e.target.value })} />
            <Input label="Code" value={editing.code ?? ""} onChange={(e) => setEditing({ ...editing, code: e.target.value })} />
            <label className="flex items-center gap-2 text-sm text-ink-700">
              <input type="checkbox" checked={editing.isActive} onChange={(e) => setEditing({ ...editing, isActive: e.target.checked })} />
              Active
            </label>
            <div className="flex justify-end gap-2 pt-1">
              <Button type="button" variant="outline" onClick={() => setEditing(null)}>Cancel</Button>
              <Button type="submit" loading={saving}>Save</Button>
            </div>
          </form>
        )}
      </Modal>
    </>
  );
}
