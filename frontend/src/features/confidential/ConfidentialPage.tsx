import { getErrorDetail } from "../../shared/api/apiError";
import { useMemo, useState } from "react";
import {
  useListPortalCredentialsQuery, useCreatePortalCredentialMutation,
  useUpdatePortalCredentialMutation, useDeletePortalCredentialMutation,
} from "../../shared/api/baseApi";
import type { PortalCredential, PortalCredentialInput } from "../../shared/api/types";
import { useConfirm } from "../../shared/components/ConfirmDialog";
import {
  Badge, Button, Card, CardBody, EmptyState, Icon, Input, Modal, PageHeader, SearchInput,
  Skeleton, Table, TBody, TD, TH, THead, TR, Textarea, useToast,
} from "../../shared/ui";

const EMPTY: PortalCredentialInput = { portalName: "", url: "", username: "", password: "", notes: "" };

/**
 * Confidential vault — shared logins for external insurance / carrier portals. Admin &
 * SuperAdmin only (route + API both gated). Passwords are encrypted at rest and shown
 * masked with a reveal / copy affordance.
 */
export function ConfidentialPage() {
  const { data: creds, isLoading } = useListPortalCredentialsQuery();
  const [create, { isLoading: creating }] = useCreatePortalCredentialMutation();
  const [update, { isLoading: updating }] = useUpdatePortalCredentialMutation();
  const [remove] = useDeletePortalCredentialMutation();
  const toast = useToast();
  const confirm = useConfirm();

  const [q, setQ] = useState("");
  const [open, setOpen] = useState(false);
  const [editing, setEditing] = useState<PortalCredential | null>(null);
  const [form, setForm] = useState<PortalCredentialInput>(EMPTY);
  const [revealed, setRevealed] = useState<Set<string>>(new Set());

  const filtered = useMemo(() => {
    const list = creds ?? [];
    const s = q.trim().toLowerCase();
    if (!s) return list;
    return list.filter((c) => `${c.portalName} ${c.username} ${c.url ?? ""} ${c.notes ?? ""}`.toLowerCase().includes(s));
  }, [creds, q]);

  function openAdd() { setEditing(null); setForm(EMPTY); setOpen(true); }
  function openEdit(c: PortalCredential) {
    setEditing(c);
    setForm({ portalName: c.portalName, url: c.url ?? "", username: c.username, password: c.password, notes: c.notes ?? "" });
    setOpen(true);
  }

  const set = (k: keyof PortalCredentialInput) =>
    (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) =>
      setForm((f) => ({ ...f, [k]: e.target.value }));

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    try {
      if (editing) { await update({ id: editing.id, ...form }).unwrap(); toast.success("Login updated"); }
      else { await create(form).unwrap(); toast.success("Login saved"); }
      setOpen(false);
    } catch (err: unknown) {
      toast.error("Couldn't save", getErrorDetail(err) ?? "Try again.");
    }
  }

  async function onDelete(c: PortalCredential) {
    if (!(await confirm({
      title: "Delete this login?",
      description: `Remove the stored login for "${c.portalName}"? This can't be undone.`,
      confirmLabel: "Delete", danger: true,
    }))) return;
    try { await remove(c.id).unwrap(); toast.success("Login deleted"); }
    catch (err: unknown) { toast.error("Couldn't delete", getErrorDetail(err) ?? "Try again."); }
  }

  function toggleReveal(id: string) {
    setRevealed((s) => { const n = new Set(s); if (n.has(id)) n.delete(id); else n.add(id); return n; });
  }
  async function copy(text: string, label: string) {
    try { await navigator.clipboard.writeText(text); toast.success("Copied", `${label} copied to clipboard.`); }
    catch { toast.error("Couldn't copy", "Your browser blocked clipboard access."); }
  }

  return (
    <>
      <PageHeader
        eyebrow="Confidential"
        title="Portal logins"
        description="Shared logins for insurance & carrier portals. Passwords are encrypted at rest and visible only to admins."
        actions={<Button leftIcon={<Icon name="plus" size={15} />} onClick={openAdd}>Add login</Button>}
      />

      <Card className="mb-4">
        <CardBody className="flex items-center gap-3 flex-wrap">
          <div className="flex-1 min-w-[240px]">
            <SearchInput value={q} onChange={setQ} placeholder="Search portals, usernames…" />
          </div>
          {creds && (
            <Badge tone="neutral" variant="soft" className="tabular-nums whitespace-nowrap">
              {filtered.length} of {creds.length}
            </Badge>
          )}
        </CardBody>
      </Card>

      {isLoading ? (
        <Card><CardBody>{[0, 1, 2].map((i) => <Skeleton key={i} className="h-12 mb-2" />)}</CardBody></Card>
      ) : filtered.length === 0 ? (
        <Card><CardBody>
          <EmptyState
            icon={<Icon name="lock" size={20} />}
            title={q ? "No matches" : "No logins yet"}
            description={q ? "No stored login matches your search." : "Add a portal login to share it securely with your admins."}
            action={!q ? <Button leftIcon={<Icon name="plus" size={15} />} onClick={openAdd}>Add login</Button> : undefined}
          />
        </CardBody></Card>
      ) : (
        <div className="overflow-x-auto">
          <Table>
            <THead>
              <TR>
                <TH>Portal</TH><TH>Username</TH><TH>Password</TH><TH>Notes</TH><TH className="text-right">Actions</TH>
              </TR>
            </THead>
            <TBody>
              {filtered.map((c) => (
                <TR key={c.id}>
                  <TD>
                    <div className="font-medium text-ink-900">{c.portalName}</div>
                    {c.url && (
                      <a href={c.url} target="_blank" rel="noreferrer"
                        className="text-xs text-brand-600 hover:underline inline-flex items-center gap-1 max-w-[240px] truncate">
                        <Icon name="externalLink" size={11} className="shrink-0" />{c.url}
                      </a>
                    )}
                  </TD>
                  <TD>
                    <button type="button" onClick={() => copy(c.username, "Username")}
                      className="font-mono text-sm text-ink-800 hover:text-brand-700 inline-flex items-center gap-1.5" title="Click to copy">
                      {c.username}<Icon name="copy" size={12} className="text-ink-400" />
                    </button>
                  </TD>
                  <TD>
                    <div className="inline-flex items-center gap-2">
                      <span className="font-mono text-sm text-ink-800 tabular-nums">{revealed.has(c.id) ? c.password : "••••••••"}</span>
                      <button type="button" onClick={() => toggleReveal(c.id)} className="text-ink-400 hover:text-ink-700"
                        title={revealed.has(c.id) ? "Hide" : "Reveal"} aria-label={revealed.has(c.id) ? "Hide password" : "Reveal password"}>
                        <Icon name={revealed.has(c.id) ? "eyeOff" : "eye"} size={14} />
                      </button>
                      <button type="button" onClick={() => copy(c.password, "Password")} className="text-ink-400 hover:text-brand-700"
                        title="Copy password" aria-label="Copy password">
                        <Icon name="copy" size={13} />
                      </button>
                    </div>
                  </TD>
                  <TD className="text-ink-600">
                    <span className="line-clamp-2 max-w-[220px]">{c.notes || <span className="text-ink-400">—</span>}</span>
                  </TD>
                  <TD>
                    <div className="flex items-center justify-end gap-1">
                      <Button variant="ghost" size="sm" onClick={() => openEdit(c)}>Edit</Button>
                      <Button variant="ghost" size="sm" onClick={() => onDelete(c)} aria-label="Delete login">
                        <Icon name="trash" size={14} className="text-rose-500" />
                      </Button>
                    </div>
                  </TD>
                </TR>
              ))}
            </TBody>
          </Table>
        </div>
      )}

      <Modal open={open} onClose={() => setOpen(false)}
        title={editing ? "Edit portal login" : "Add portal login"}
        description="Stored securely — the password is encrypted at rest."
        size="lg"
        footer={
          <>
            <Button variant="ghost" onClick={() => setOpen(false)}>Cancel</Button>
            <Button form="cred-form" type="submit" loading={creating || updating}>{editing ? "Save" : "Add login"}</Button>
          </>
        }>
        <form id="cred-form" onSubmit={submit} className="grid grid-cols-1 gap-3">
          <Input label="Portal name" required placeholder="e.g. Mutual of Omaha — agent portal" value={form.portalName} onChange={set("portalName")} />
          <Input label="URL" placeholder="https://…" value={form.url ?? ""} onChange={set("url")} />
          <Input label="Username" required value={form.username} onChange={set("username")} />
          <Input label="Password" required value={form.password} onChange={set("password")} />
          <Textarea label="Notes" placeholder="MFA hints, which team uses it, etc." value={form.notes ?? ""} onChange={set("notes")} />
        </form>
      </Modal>
    </>
  );
}
