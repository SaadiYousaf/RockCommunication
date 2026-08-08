import { useMemo, useRef, useState } from "react";
import { Avatar, Textarea, cn } from "../../shared/ui";

export interface MentionUser { id: string; userName: string; }

/**
 * A text field (single- or multi-line) with @mention autocomplete: typing "@" opens a dropdown of
 * teammates, arrow keys navigate, Enter/Tab inserts. Submit is Enter (single-line) / ⌘·Ctrl+Enter
 * (multi-line) — suppressed while the mention menu is open so Enter picks a name instead.
 */
export function MentionBox({
  value, onChange, onSubmit, users, multiline = false, placeholder, rows = 2, ariaLabel, inputClassName,
}: {
  value: string;
  onChange: (v: string) => void;
  onSubmit?: () => void;
  users: MentionUser[];
  multiline?: boolean;
  placeholder?: string;
  rows?: number;
  ariaLabel?: string;
  inputClassName?: string;
}) {
  const ref = useRef<HTMLTextAreaElement | HTMLInputElement | null>(null);
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [anchor, setAnchor] = useState(0);   // index of the '@' being completed
  const [active, setActive] = useState(0);

  const matches = useMemo(() => {
    if (!open) return [];
    const q = query.toLowerCase();
    return users.filter((u) => u.userName.toLowerCase().includes(q)).slice(0, 6);
  }, [open, query, users]);

  function detect(v: string, caret: number) {
    const before = v.slice(0, caret);
    const m = before.match(/(?:^|\s)@([A-Za-z0-9._-]*)$/);
    if (m) {
      setOpen(true);
      setQuery(m[1]);
      setAnchor(caret - m[1].length - 1);
      setActive(0);
    } else {
      setOpen(false);
    }
  }

  function handleChange(e: React.ChangeEvent<HTMLTextAreaElement | HTMLInputElement>) {
    onChange(e.target.value);
    detect(e.target.value, e.target.selectionStart ?? e.target.value.length);
  }

  function insert(u: MentionUser) {
    const el = ref.current;
    const caret = el?.selectionStart ?? value.length;
    const before = value.slice(0, anchor);
    const after = value.slice(caret);
    const inserted = `${before}@${u.userName} ${after}`;
    onChange(inserted);
    setOpen(false);
    const pos = `${before}@${u.userName} `.length;
    requestAnimationFrame(() => { el?.setSelectionRange(pos, pos); el?.focus(); });
  }

  function handleKeyDown(e: React.KeyboardEvent) {
    if (open && matches.length) {
      if (e.key === "ArrowDown") { e.preventDefault(); setActive((a) => (a + 1) % matches.length); return; }
      if (e.key === "ArrowUp") { e.preventDefault(); setActive((a) => (a - 1 + matches.length) % matches.length); return; }
      if (e.key === "Enter" || e.key === "Tab") { e.preventDefault(); insert(matches[active]); return; }
      if (e.key === "Escape") { setOpen(false); return; }
    }
    if (!onSubmit) return;
    if (multiline && (e.metaKey || e.ctrlKey) && e.key === "Enter") { e.preventDefault(); onSubmit(); }
    else if (!multiline && e.key === "Enter" && !e.shiftKey && !open) { e.preventDefault(); onSubmit(); }
  }

  const menu = open && matches.length > 0 && (
    <div className="absolute z-20 mt-1 left-0 w-60 max-w-[80vw] rounded-xl border border-ink-200 bg-white shadow-pop py-1">
      <div className="px-3 pb-1 pt-0.5 text-[10px] uppercase tracking-wide text-ink-400">Mention a teammate</div>
      {matches.map((u, i) => (
        <button key={u.id} type="button" onMouseDown={(ev) => { ev.preventDefault(); insert(u); }}
          className={cn("w-full text-left px-3 py-1.5 text-sm flex items-center gap-2",
            i === active ? "bg-brand-50 text-brand-700" : "hover:bg-ink-50 text-ink-700")}>
          <Avatar name={u.userName} size={22} /> {u.userName}
        </button>
      ))}
    </div>
  );

  return (
    <div className="relative">
      {multiline ? (
        <Textarea ref={ref as React.Ref<HTMLTextAreaElement>} value={value} rows={rows}
          onChange={handleChange} onKeyDown={handleKeyDown} placeholder={placeholder} />
      ) : (
        <input ref={ref as React.Ref<HTMLInputElement>} value={value} aria-label={ariaLabel}
          onChange={handleChange} onKeyDown={handleKeyDown} placeholder={placeholder} className={inputClassName} />
      )}
      {menu}
    </div>
  );
}
