import { useEffect, useRef, useState } from "react";
import { useSelector } from "react-redux";
import type { RootState } from "../../app/store";
import { API_URL } from "../../shared/config";
import { Icon } from "../../shared/ui";

type ImageKind = "Photo" | "IdCardFront" | "IdCardBack";

/**
 * Upload + preview for one employee image slot. Images are served through an authorized
 * endpoint, so we fetch them as a blob with the bearer token and render an object URL
 * (the same protected-content pattern the Documents viewer uses).
 */
export function EmployeeImageField({
  employeeId, kind, label, hasImage, onChanged,
}: {
  employeeId: string;
  kind: ImageKind;
  label: string;
  hasImage: boolean;
  onChanged: () => void;
}) {
  const token = useSelector((s: RootState) => s.auth.accessToken) ?? "";
  const [preview, setPreview] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (!hasImage) { setPreview(null); return; }
    let url: string | null = null;
    let cancelled = false;
    (async () => {
      try {
        const res = await fetch(`${API_URL}/api/hr/employees/${employeeId}/image/${kind}`, {
          headers: { Authorization: `Bearer ${token}` },
        });
        if (!res.ok || cancelled) return;
        const blob = await res.blob();
        if (cancelled) return;
        url = URL.createObjectURL(blob);
        setPreview(url);
      } catch { /* preview is best-effort */ }
    })();
    return () => { cancelled = true; if (url) URL.revokeObjectURL(url); };
  }, [employeeId, kind, hasImage, token]);

  async function onSelect(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (!file) return;
    setBusy(true); setError(null);
    try {
      const fd = new FormData();
      fd.append("file", file);
      const res = await fetch(`${API_URL}/api/hr/employees/${employeeId}/image?kind=${kind}`, {
        method: "POST", headers: { Authorization: `Bearer ${token}` }, body: fd,
      });
      if (res.ok) onChanged();
      else setError("Upload failed — use an image under 10 MB.");
    } catch {
      setError("Upload failed. Try again.");
    } finally {
      setBusy(false);
      if (inputRef.current) inputRef.current.value = "";
    }
  }

  return (
    <div>
      <div className="text-xs font-medium text-ink-600 mb-1.5">{label}</div>
      <div className="flex items-center gap-3">
        <div className="h-20 w-28 rounded-lg border hairline bg-ink-50 grid place-items-center overflow-hidden shrink-0">
          {preview
            ? <img src={preview} alt={label} className="h-full w-full object-cover" />
            : <Icon name="upload" size={18} className="text-ink-300" />}
        </div>
        <div className="space-y-1">
          <input ref={inputRef} type="file" accept="image/*" className="hidden" onChange={onSelect} />
          <button type="button" onClick={() => inputRef.current?.click()} disabled={busy}
            className="text-sm px-3 py-1.5 rounded-lg border hairline text-ink-700 hover:bg-ink-50 disabled:opacity-50 transition-colors">
            {busy ? "Uploading…" : preview ? "Replace" : "Upload"}
          </button>
          {error && <div className="text-[11px] text-rose-600">{error}</div>}
        </div>
      </div>
    </div>
  );
}
