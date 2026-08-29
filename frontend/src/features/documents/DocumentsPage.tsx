type MammothLib = { convertToHtml(input: { arrayBuffer: ArrayBuffer }): Promise<{ value: string }> };
import { API_URL } from "../../shared/config";
import { getErrorDetail } from "../../shared/api/apiError";
import { useEffect, useMemo, useRef, useState } from "react";
import { useSelector } from "react-redux";
import DOMPurify from "dompurify";
import {
  useListDocumentsQuery, useUploadDocumentMutation, useDeleteDocumentMutation,
  useDocumentNotesQuery, useAddDocumentNoteMutation,
} from "../../shared/api/baseApi";
import type { DocumentMeta } from "../../shared/api/types";
import type { RootState } from "../../app/store";
import {
  Badge, Button, Card, CardBody, CardHeader, EmptyState, ErrorState, Icon, InfoHint, Input,
  PageHeader, Skeleton, useToast,
} from "../../shared/ui";
import { useConfirm } from "../../shared/components/ConfirmDialog";
import { MESSAGES } from "../../shared/constants/messages";
import { DOCUMENTS_MSG } from "./messages";


function fmtSize(n: number) {
  if (n < 1024) return `${n} B`;
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(0)} KB`;
  return `${(n / (1024 * 1024)).toFixed(1)} MB`;
}

export function DocumentsPage() {
  const auth = useSelector((s: RootState) => s.auth);
  const roles = auth.user?.roles ?? [];
  const canManage = roles.some((r) => ["Admin", "ProgramManager", "SuperAdmin"].includes(r));

  const { data: docs, isLoading, isError, error, refetch } = useListDocumentsQuery();
  const [uploadDoc, { isLoading: uploading }] = useUploadDocumentMutation();
  const [deleteDoc] = useDeleteDocumentMutation();
  const toast = useToast();
  const confirm = useConfirm();
  const fileRef = useRef<HTMLInputElement>(null);
  const [name, setName] = useState("");
  const [pending, setPending] = useState<File | null>(null);
  const [active, setActive] = useState<DocumentMeta | null>(null);

  async function doUpload() {
    if (!pending) return;
    try {
      await uploadDoc({ name: name.trim() || pending.name, file: pending }).unwrap();
      toast.success(DOCUMENTS_MSG.uploaded, pending.name);
      setPending(null); setName("");
      if (fileRef.current) fileRef.current.value = "";
    } catch (err: unknown) {
      toast.error(DOCUMENTS_MSG.uploadFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
    }
  }

  return (
    <>
      <PageHeader
        eyebrow="Operations"
        title="Documents"
        description="Shared Word documents and spreadsheets. Open to read in the protected viewer — copying, printing and downloading are disabled."
      />

      {canManage && (
        <Card className="mb-5">
          <CardHeader
            title={<span className="inline-flex items-center gap-2"><Icon name="upload" size={16} className="text-ink-400" />Upload a document</span>}
            subtitle="Word (.doc/.docx) or spreadsheets (.xls/.xlsx/.csv). Max 30 MB."
          />
          <CardBody>
            <div className="grid grid-cols-1 sm:grid-cols-[1fr_auto] gap-3 items-end">
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                <Input
                  label="Display name (optional)"
                  placeholder="Defaults to the file name"
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                />
                <div className="flex flex-col gap-1.5">
                  <label className="text-[12px] font-medium text-ink-700 leading-none">File</label>
                  <input
                    ref={fileRef}
                    type="file"
                    accept=".doc,.docx,.rtf,.odt,.xls,.xlsx,.csv,.ods"
                    onChange={(e) => setPending(e.target.files?.[0] ?? null)}
                    className="block w-full text-sm text-ink-700 rounded-md file:mr-3 file:rounded-md file:border-0 file:bg-ink-900 file:px-3 file:py-2 file:text-white file:text-xs file:transition-colors hover:file:bg-ink-800 focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-500 focus-visible:ring-offset-2"
                  />
                </div>
              </div>
              <Button onClick={doUpload} loading={uploading} disabled={!pending}
                title={!pending ? "Choose a file to upload first" : undefined}
                leftIcon={<Icon name="plus" size={16} />}>
                Upload
              </Button>
            </div>
          </CardBody>
        </Card>
      )}

      <div className="grid grid-cols-1 lg:grid-cols-[340px_1fr] gap-5">
        {/* List */}
        <Card>
          <CardHeader
            title={<span className="inline-flex items-center gap-2"><Icon name="book" size={16} className="text-ink-400" />Library</span>}
            subtitle={<span className="tabular-nums">{docs?.length ?? 0} document{(docs?.length ?? 0) === 1 ? "" : "s"}</span>}
          />
          <CardBody className="pt-0">
            {isLoading ? (
              <div className="space-y-2">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-12" />)}</div>
            ) : isError ? (
              // A failed request must NEVER fall through to "no documents yet" — that reads as data loss.
              <ErrorState error={error} resource={DOCUMENTS_MSG.resourceName} onRetry={refetch} />
            ) : !docs || docs.length === 0 ? (
              <EmptyState icon={<Icon name="doc" size={20} />} title={DOCUMENTS_MSG.noDocumentsTitle}
                description={canManage ? DOCUMENTS_MSG.noDocumentsManageDesc : DOCUMENTS_MSG.noDocumentsReadonlyDesc} />
            ) : (
              <ul className="space-y-1">
                {docs.map((d) => (
                  <li key={d.id}>
                    <button
                      onClick={() => setActive(d)}
                      className={`w-full text-left flex items-center gap-3 px-3 py-2.5 rounded-lg transition-colors ${
                        active?.id === d.id ? "bg-brand-50 ring-1 ring-brand-200" : "hover:bg-ink-50"
                      }`}
                    >
                      <span className={`h-9 w-9 rounded-lg grid place-items-center shrink-0 ${
                        d.kind === "spreadsheet" ? "bg-emerald-50 text-emerald-600" : "bg-brand-50 text-brand-600"
                      }`}>
                        <Icon name={d.kind === "spreadsheet" ? "spreadsheet" : "doc"} size={16} />
                      </span>
                      <span className="min-w-0 flex-1">
                        <span className="block text-sm font-medium text-ink-900 truncate">{d.name}</span>
                        <span className="block text-[11px] text-ink-500 truncate tabular-nums">
                          {d.kind} · {fmtSize(d.size)} · {new Date(d.createdAt).toLocaleDateString()}
                        </span>
                      </span>
                      {canManage && (
                        <span
                          role="button"
                          tabIndex={0}
                          title="Delete document"
                          aria-label={`Delete ${d.name}`}
                          onClick={async (e) => {
                            e.stopPropagation();
                            if (!(await confirm({
                              title: DOCUMENTS_MSG.deleteConfirmTitle(d.name),
                              description: DOCUMENTS_MSG.deleteConfirmDesc,
                              confirmLabel: "Delete",
                              danger: true,
                            }))) return;
                            try {
                              await deleteDoc(d.id).unwrap();
                              if (active?.id === d.id) setActive(null);
                              toast.success(DOCUMENTS_MSG.deleted, d.name);
                            } catch (err: unknown) { toast.error(DOCUMENTS_MSG.deleteFailed, getErrorDetail(err) ?? MESSAGES.tryAgain); }
                          }}
                          className="text-ink-400 hover:text-rose-600 hover:bg-rose-50 p-1 rounded-md transition-colors shrink-0"
                        >
                          <Icon name="x" size={14} />
                        </span>
                      )}
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </CardBody>
        </Card>

        {/* Viewer */}
        {active
          ? <ProtectedViewer doc={active} token={auth.accessToken ?? ""} viewer={auth.user?.userName ?? "user"} />
          : (
            <Card>
              <CardBody>
                <EmptyState icon={<Icon name="doc" size={20} />} title={DOCUMENTS_MSG.selectDocumentTitle}
                  description={DOCUMENTS_MSG.selectDocumentDesc} />
              </CardBody>
            </Card>
          )}
      </div>
    </>
  );
}

/* ------------------------------------------------------------------ */
/* Protected viewer — renders client-side, blocks copy/print/download. */
/* ------------------------------------------------------------------ */

function ProtectedViewer({ doc, token, viewer }: { doc: DocumentMeta; token: string; viewer: string }) {
  const [html, setHtml] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  // Fetch + render. We never expose a download — bytes are converted to inert HTML.
  useEffect(() => {
    let cancelled = false;
    setLoading(true); setHtml(null); setError(null);
    (async () => {
      try {
        const res = await fetch(`${API_URL}/api/documents/${doc.id}/content`, {
          headers: { Authorization: `Bearer ${token}` },
        });
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        const buf = await res.arrayBuffer();
        let rendered = "";
        if (doc.kind === "spreadsheet") {
          // Lazy-import xlsx so it doesn't bloat the main bundle (mirrors mammoth below).
          const XLSX = await import("xlsx");
          const wb = XLSX.read(buf, { type: "array" });
          rendered = wb.SheetNames.map((sn) => {
            const sheetHtml = XLSX.utils.sheet_to_html(wb.Sheets[sn]);
            return `<h3 class="doc-sheet-title">${escapeHtml(sn)}</h3>${sheetHtml}`;
          }).join("");
        } else {
          // Lazy-import mammoth so it doesn't bloat the main bundle.
          const mammoth = (await import("mammoth")).default ?? (await import("mammoth"));
          const out = await (mammoth as unknown as MammothLib).convertToHtml({ arrayBuffer: buf });
          rendered = out.value || "<p><em>(empty document)</em></p>";
        }
        // Sanitize converter output before it ever touches the DOM. mammoth/xlsx emit
        // attacker-controlled HTML from the uploaded file (a crafted cell/hyperlink can
        // inject <img onerror> / javascript: URIs). DOMPurify strips scripts, event
        // handlers and dangerous URIs while keeping tables/formatting. Also forbid iframes.
        const safe = DOMPurify.sanitize(rendered, {
          FORBID_TAGS: ["script", "iframe", "object", "embed", "form", "style"],
          FORBID_ATTR: ["srcdoc"],
        });
        if (!cancelled) setHtml(safe);
      } catch (e) {
        if (!cancelled) setError(DOCUMENTS_MSG.renderFailed);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [doc.id, doc.kind, token]);

  // Block print while a protected doc is open (covers Ctrl/Cmd+P and menu print).
  useEffect(() => {
    const style = document.createElement("style");
    style.setAttribute("data-doc-guard", "");
    style.textContent = "@media print { body { display: none !important; } }";
    document.head.appendChild(style);

    const onKey = (e: KeyboardEvent) => {
      const k = e.key.toLowerCase();
      if ((e.ctrlKey || e.metaKey) && ["c", "x", "s", "p", "u"].includes(k)) {
        e.preventDefault();
        e.stopPropagation();
      }
    };
    window.addEventListener("keydown", onKey, true);
    return () => {
      document.head.removeChild(style);
      window.removeEventListener("keydown", onKey, true);
    };
  }, [doc.id]);

  const block = (e: React.SyntheticEvent) => { e.preventDefault(); return false; };
  const watermark = useMemo(() => {
    const stamp = `${viewer} · ${new Date().toLocaleString()}`;
    return Array.from({ length: 36 }).map((_, i) => (
      <span key={i} className="text-ink-900/[0.06] text-sm font-semibold whitespace-nowrap select-none">
        {stamp}
      </span>
    ));
  }, [viewer]);

  return (
    <Card>
      <CardHeader
        title={doc.name}
        subtitle={`${doc.kind} · view-only`}
        action={
          <span className="inline-flex items-center gap-1.5">
            <Badge tone="warning" variant="soft" dot>Protected — no copy / print / download</Badge>
            <InfoHint title="Protected view" side="left">Your name and the time are stamped faintly across every page, and copying, printing, and downloading are switched off. Read it here instead of saving a copy.</InfoHint>
          </span>
        }
      />
      <CardBody>
        <div
          className="relative max-h-[70vh] overflow-auto rounded-lg border border-ink-200 bg-white"
          onContextMenu={block}
          onCopy={block}
          onCut={block}
          onDragStart={block}
          style={{ userSelect: "none", WebkitUserSelect: "none" }}
        >
          {/* Tiled watermark overlay */}
          <div
            aria-hidden
            className="pointer-events-none absolute inset-0 z-10 flex flex-wrap gap-x-10 gap-y-16 p-8 -rotate-[24deg] origin-center overflow-hidden"
          >
            {watermark}
          </div>

          <div className="relative z-0 p-6 doc-render">
            {loading && <div className="space-y-2">{[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-5" />)}</div>}
            {error && <div className="text-sm text-rose-600">{error}</div>}
            {html && (
              <div
                // Rendered, inert HTML. Selection/copy already blocked at the container.
                dangerouslySetInnerHTML={{ __html: html }}
              />
            )}
          </div>
        </div>

        <NotesPanel documentId={doc.id} />
      </CardBody>
    </Card>
  );
}

function NotesPanel({ documentId }: { documentId: string }) {
  const { data: notes, isLoading } = useDocumentNotesQuery(documentId);
  const [addNote, { isLoading: adding }] = useAddDocumentNoteMutation();
  const [body, setBody] = useState("");
  const toast = useToast();

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!body.trim()) return;
    try {
      await addNote({ id: documentId, body: body.trim() }).unwrap();
      setBody("");
    } catch (err: unknown) {
      toast.error(DOCUMENTS_MSG.saveNoteFailed, getErrorDetail(err) ?? MESSAGES.tryAgain);
    }
  }

  return (
    <div className="mt-5 border-t hairline pt-4">
      <div className="text-sm font-semibold text-ink-900 mb-2 flex items-center gap-2">
        <Icon name="chat" size={14} /> Notes
        <span className="text-xs font-normal text-ink-500">— write here instead of editing the file</span>
      </div>
      {isLoading ? (
        <Skeleton className="h-10" />
      ) : notes && notes.length > 0 ? (
        <ul className="space-y-2 mb-3 max-h-48 overflow-auto">
          {notes.map((n) => (
            <li key={n.id} className="text-sm bg-ink-50 rounded-lg px-3 py-2 transition-colors hover:bg-ink-100/70">
              <div className="text-ink-800 whitespace-pre-wrap">{n.body}</div>
              <div className="text-[11px] text-ink-500 mt-1 tabular-nums whitespace-nowrap">{new Date(n.createdAt).toLocaleString()}</div>
            </li>
          ))}
        </ul>
      ) : (
        <div className="flex items-center gap-2 text-xs text-ink-500 mb-3">
          <Icon name="chat" size={14} className="text-ink-400" /> No notes yet — add the first one below.
        </div>
      )}
      <form onSubmit={submit} className="flex gap-2">
        <Input
          placeholder="Add a note about this document…"
          value={body}
          onChange={(e) => setBody(e.target.value)}
          containerClassName="flex-1"
        />
        <Button type="submit" loading={adding} disabled={!body.trim()}>Add</Button>
      </form>
    </div>
  );
}

function escapeHtml(s: string) {
  return s.replace(/[&<>"']/g, (c) =>
    ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c] as string));
}
