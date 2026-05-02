import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Icon, Input } from "../ui";

/**
 * Global header search.
 * - Submits on Enter to /search?q=...
 * - Shows the current `q` when on /search so the box reflects the active query.
 * - Cmd/Ctrl+K focuses the field from anywhere.
 */
export function HeaderSearch() {
  const [q, setQ] = useState("");
  const navigate = useNavigate();
  const inputRef = useRef<HTMLInputElement | null>(null);

  // Sync from URL when the path changes (back/forward, refresh).
  useEffect(() => {
    if (typeof window === "undefined") return;
    const sync = () => {
      if (window.location.pathname.startsWith("/search")) {
        const sp = new URLSearchParams(window.location.search);
        setQ(sp.get("q") ?? "");
      }
    };
    sync();
    window.addEventListener("popstate", sync);
    return () => window.removeEventListener("popstate", sync);
  }, []);

  // Cmd/Ctrl+K — quick focus from anywhere.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === "k") {
        e.preventDefault();
        inputRef.current?.focus();
        inputRef.current?.select();
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  function submit() {
    const v = q.trim();
    if (v.length < 2) return;
    navigate(`/search?q=${encodeURIComponent(v)}`);
  }

  return (
    <Input
      ref={inputRef}
      placeholder="Search leads, users, sales…   ⌘K"
      value={q}
      onChange={(e) => setQ(e.target.value)}
      onKeyDown={(e) => { if (e.key === "Enter") submit(); }}
      leftIcon={<Icon name="search" size={16} />}
      className="h-10 bg-ink-50/80 border-transparent focus:bg-white"
    />
  );
}
