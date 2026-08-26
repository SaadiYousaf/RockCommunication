import { getErrorDetail } from "../../shared/api/apiError";
import { useMemo, useState } from "react";
import { useAddDncMutation, useListDncQuery, useRemoveDncMutation } from "../../shared/api/baseApi";
import {
  Badge, BulkActionBar, Button, Card, CardBody, Checkbox, EmptyState, Icon, InfoHint, Input, Modal, PageHeader,
  SearchInput, Skeleton, Stat, Table, TBody, TD, TH, THead, TR, useToast,
} from "../../shared/ui";
import { Can, Perm } from "../../shared/auth/permissions";
import { useTableSort } from "../../shared/hooks/useTableSort";
import { useRowSelection } from "../../shared/hooks/useRowSelection";
import { exportRowsToCsv } from "../../shared/lib/csv";
import { useConfirm } from "../../shared/components/ConfirmDialog";
import { MESSAGES } from "../../shared/constants/messages";
import { CALLCENTER_MSG } from "./messages";

export function DncPage() {
  const { data: list, isLoading } = useListDncQuery();
  const [add, { isLoading: adding }] = useAddDncMutation();
  const [remove, { isLoading: removing }] = useRemoveDncMutation();
  const toast = useToast();
  const confirm = useConfirm();

  const [open, setOpen] = useState(false);
  const [phone, setPhone] = useState("");
  const [reason, setReason] = useState("");
  const [search, setSearch] = useState("");
  const [confirmRemove, setConfirmRemove] = useState<any | null>(null);
  const [bulkRemoving, setBulkRemoving] = useState(false);

  const filtered = useMemo(() => {
    if (!list) return [];
    const q = search.trim().toLowerCase();
    if (!q) return list;
    return list.filter((e) =>
      e.phoneNormalized?.toLowerCase().includes(q) ||
      e.reason?.toLowerCase().includes(q),
    );
  }, [list, search]);

  const { sorted, dirFor, toggle } = useTableSort(filtered);

  const sel = useRowSelection(sorted.map((e) => e.id));

  function exportSelected() {
    const chosen = sorted.filter((e) => sel.isSelected(e.id));
    exportRowsToCsv(chosen, [
      { header: "Number", value: (e) => e.phoneNormalized },
      { header: "Reason/note", value: (e) => e.reason ?? "" },
      { header: "Added", value: (e) => e.source },
    ], `dnc-${new Date().toISOString().slice(0, 10)}.csv`);
    toast.success(CALLCENTER_MSG.exportReadyTitle, CALLCENTER_MSG.rowsDownloaded(chosen.length));
  }

  async function removeSelected() {
    const ids = sel.selectedIds;
    if (ids.length === 0) return;
    if (!(await confirm({
      title: CALLCENTER_MSG.removeDncConfirmTitle(ids.length),
      description: CALLCENTER_MSG.removeDncConfirmBody,
      confirmLabel: CALLCENTER_MSG.removeLabel,
      danger: true,
    }))) return;
    setBulkRemoving(true);
    try {
      await Promise.all(ids.map((id) => remove(id).unwrap()));
      toast.success(CALLCENTER_MSG.removedFromDncCount(ids.length));
      sel.clear();
    } catch (err: unknown) {
      toast.error(CALLCENTER_MSG.removeFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
    } finally {
      setBulkRemoving(false);
    }
  }

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    try {
      await add({ phone, reason: reason || undefined }).unwrap();
      toast.success(CALLCENTER_MSG.addedToDncTitle, phone);
      setPhone(""); setReason(""); setOpen(false);
    } catch (err: unknown) {
      toast.error(CALLCENTER_MSG.addDncFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
    }
  }

  async function doRemove(id: string) {
    try {
      await remove(id).unwrap();
      toast.success(CALLCENTER_MSG.removedFromDnc);
      setConfirmRemove(null);
    } catch (err: unknown) {
      toast.error(CALLCENTER_MSG.removeFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
    }
  }

  return (
    <>
      <PageHeader
        eyebrow="Call center"
        title="Do Not Call list"
        description="Phone numbers your team is prohibited from contacting. Auto-checked before every dial."
        actions={<Can permission={Perm.DncManage}><Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setOpen(true)}>Add number</Button></Can>}
      />

      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4 mb-5">
        <Stat label="Total entries" value={list?.length ?? 0}
              icon={<Icon name="flag" size={16} />} tone="danger" />
        <Stat label="Active blocks" value={list?.filter((e) => !e.expiresAt || new Date(e.expiresAt) > new Date()).length ?? 0}
              icon={<Icon name="shield" size={16} />} tone="warning" hint="Currently blocking calls" />
        <Stat label="Expired" value={list?.filter((e) =>  e.expiresAt && new Date(e.expiresAt) <= new Date()).length ?? 0}
              icon={<Icon name="clock" size={16} />} tone="neutral" hint="Past expiry, no longer blocking" />
      </div>

      <Card className="mb-4">
        <CardBody>
          <SearchInput
            placeholder={CALLCENTER_MSG.dncSearchPlaceholder}
            value={search} onChange={setSearch}
          />
        </CardBody>
      </Card>

      {isLoading ? (
        <Card><CardBody>{[0, 1, 2].map((i) => <Skeleton key={i} className="h-12 mb-2" />)}</CardBody></Card>
      ) : filtered.length === 0 ? (
        <Card><CardBody>
          <EmptyState
            icon={<Icon name="flag" size={20} />}
            title={search ? CALLCENTER_MSG.noMatchingEntriesTitle : CALLCENTER_MSG.noDncEntriesTitle}
            description={search ? CALLCENTER_MSG.tryDifferentSearch : CALLCENTER_MSG.noDncEntriesBody}
            action={!search
              ? <Button leftIcon={<Icon name="plus" size={16} />} onClick={() => setOpen(true)}>Add number</Button>
              : undefined}
          />
        </CardBody></Card>
      ) : (
        <>
        <Table>
          <THead>
            <TR>
              <TH className="w-10"><Checkbox aria-label="Select all numbers" {...sel.allCheckboxProps} /></TH>
              <TH sortDir={dirFor("phoneNormalized")} onClick={() => toggle("phoneNormalized")}>
                <span className="inline-flex items-center gap-1">
                  Phone
                  <InfoHint title="Phone" side="top">
                    Stored in normalized form (standardized digits, no formatting) so the number stays blocked no matter how it's typed or dialed.
                  </InfoHint>
                </span>
              </TH>
              <TH sortDir={dirFor("reason")} onClick={() => toggle("reason")}>Reason</TH>
              <TH sortDir={dirFor("source")} onClick={() => toggle("source")}>
                <span className="inline-flex items-center gap-1">
                  Source
                  <InfoHint title="Source" side="top">
                    Where this entry came from — e.g. added manually or imported from an external suppression list.
                  </InfoHint>
                </span>
              </TH>
              <TH sortDir={dirFor("expiresAt")} onClick={() => toggle("expiresAt")}>
                <span className="inline-flex items-center gap-1">
                  Expires
                  <InfoHint title="Expires" side="top">
                    When this block lifts and the number becomes callable again; permanent entries never expire.
                  </InfoHint>
                </span>
              </TH>
              <TH className="text-right">Actions</TH>
            </TR>
          </THead>
          <TBody>
            {sorted.map((e) => {
              const expired = e.expiresAt && new Date(e.expiresAt) <= new Date();
              return (
                <TR key={e.id} className={sel.isSelected(e.id) ? "bg-brand-50/40" : undefined}>
                  <TD>
                    <Checkbox aria-label={`Select ${e.phoneNormalized}`} {...sel.checkboxProps(e.id)} />
                  </TD>
                  <TD className="font-mono text-ink-900 tabular-nums whitespace-nowrap">{e.phoneNormalized}</TD>
                  <TD className="text-ink-600 max-w-[280px] truncate">{e.reason ?? <span className="text-ink-400">—</span>}</TD>
                  <TD>
                    <Badge tone="neutral" variant="soft">{e.source ?? "manual"}</Badge>
                  </TD>
                  <TD>
                    {e.expiresAt
                      ? <Badge tone={expired ? "neutral" : "warning"} variant="soft" className="tabular-nums whitespace-nowrap">
                          {new Date(e.expiresAt).toLocaleDateString()}
                        </Badge>
                      : <Badge tone="danger" variant="soft" dot>Permanent</Badge>}
                  </TD>
                  <TD>
                    <div className="flex justify-end">
                      <Can permission={Perm.DncManage}>
                        <Button variant="ghost" size="sm" className="text-rose-600 hover:bg-rose-50"
                          leftIcon={<Icon name="trash" size={13} />}
                          onClick={() => setConfirmRemove(e)}>
                          Remove
                        </Button>
                      </Can>
                    </div>
                  </TD>
                </TR>
              );
            })}
          </TBody>
        </Table>
        <BulkActionBar
          count={sel.selectedCount} itemNoun="number" onClear={sel.clear}
          actions={[
            { key: "csv", label: "Export CSV", icon: "download", onClick: exportSelected },
            { key: "remove", label: "Remove", icon: "trash", onClick: removeSelected, danger: true, loading: bulkRemoving },
          ]}
        />
        </>
      )}

      <Modal
        open={open} onClose={() => setOpen(false)}
        title="Add to DNC list"
        description="The number will be normalized and blocked from outbound dialing."
        size="md"
        footer={
          <>
            <Button variant="ghost" onClick={() => setOpen(false)}>Cancel</Button>
            <Button form="dnc-form" type="submit" loading={adding}>Add to DNC</Button>
          </>
        }
      >
        <form id="dnc-form" onSubmit={submit} className="grid grid-cols-1 gap-3">
          <Input label="Phone number" required placeholder="(555) 555-5555 or +15555555555"
            value={phone} onChange={(e) => setPhone(e.target.value)}
            leftIcon={<Icon name="phone" size={14} />} autoFocus />
          <Input label="Reason" value={reason} onChange={(e) => setReason(e.target.value)}
            placeholder="Customer requested DNC, complaint, etc." />
        </form>
      </Modal>

      <Modal
        open={confirmRemove !== null}
        onClose={() => setConfirmRemove(null)}
        title="Remove from DNC?"
        description={confirmRemove ? `${confirmRemove.phoneNormalized} will be eligible for dialing again.` : ""}
        footer={
          <>
            <Button variant="ghost" onClick={() => setConfirmRemove(null)}>Cancel</Button>
            <Button variant="danger" loading={removing} onClick={() => confirmRemove && doRemove(confirmRemove.id)}>Remove</Button>
          </>
        }
      >
        <div className="text-sm text-ink-700">
          Re-adding the number to DNC later is always possible.
        </div>
      </Modal>
    </>
  );
}

