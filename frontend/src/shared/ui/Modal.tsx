import { useEffect, useId, useRef, type ReactNode } from "react";
import { cn } from "./cn";

export function Modal({
  open, onClose, title, description, children, footer, size = "md",
  closeOnBackdrop = true, hideAccentBar = false,
}: {
  open: boolean;
  onClose: () => void;
  title?: ReactNode;
  description?: ReactNode;
  children: ReactNode;
  footer?: ReactNode;
  size?: "sm" | "md" | "lg" | "xl" | "2xl";
  /** If false, clicking the dimmed backdrop won't close the modal. */
  closeOnBackdrop?: boolean;
  /** Hide the brand→accent top stripe — useful for purely informational dialogs. */
  hideAccentBar?: boolean;
}) {
  const dialogRef = useRef<HTMLDivElement>(null);
  const titleId = useId();

  // Keep onClose in a ref so the open/lifecycle effect below has stable deps.
  // Without this, every parent render creates a new `onClose` arrow, the effect
  // re-runs on every keystroke, and focus snaps back to the first input.
  const onCloseRef = useRef(onClose);
  useEffect(() => { onCloseRef.current = onClose; }, [onClose]);

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") { onCloseRef.current(); return; }
      // Trap Tab within the dialog so focus can't move to the blurred, still-interactive background.
      if (e.key !== "Tab" || !dialogRef.current) return;
      const nodes = dialogRef.current.querySelectorAll<HTMLElement>(
        "input, select, textarea, button, [href], [tabindex]:not([tabindex='-1'])"
      );
      const list = Array.from(nodes).filter((n) => !n.hasAttribute("disabled") && n.offsetParent !== null);
      if (list.length === 0) { e.preventDefault(); return; }
      const first = list[0];
      const last = list[list.length - 1];
      const active = document.activeElement as HTMLElement | null;
      const inside = dialogRef.current.contains(active);
      if (e.shiftKey && (active === first || !inside)) { e.preventDefault(); last.focus(); }
      else if (!e.shiftKey && (active === last || !inside)) { e.preventDefault(); first.focus(); }
    };
    window.addEventListener("keydown", onKey);
    // Lock scroll on body, but pad to avoid layout shift from disappearing scrollbar
    const scrollbarWidth = window.innerWidth - document.documentElement.clientWidth;
    const prevPad = document.body.style.paddingRight;
    document.body.style.overflow = "hidden";
    if (scrollbarWidth > 0) document.body.style.paddingRight = `${scrollbarWidth}px`;

    // Move focus into the dialog for keyboard users — once, on open.
    const previousActive = document.activeElement as HTMLElement | null;
    requestAnimationFrame(() => {
      const focusable = dialogRef.current?.querySelector<HTMLElement>(
        "input, select, textarea, button, [href], [tabindex]:not([tabindex='-1'])"
      );
      focusable?.focus();
    });

    return () => {
      window.removeEventListener("keydown", onKey);
      document.body.style.overflow = "";
      document.body.style.paddingRight = prevPad;
      previousActive?.focus?.();
    };
  }, [open]);

  if (!open) return null;
  const sizes = {
    sm:  "max-w-sm",
    md:  "max-w-md",
    lg:  "max-w-lg",
    xl:  "max-w-2xl",
    "2xl": "max-w-3xl",
  } as const;

  return (
    <div className="fixed inset-0 z-50 grid place-items-center p-4 animate-fade-in">
      {/* Backdrop — slightly stronger blur and a subtle radial vignette so the modal
         feels "spotlit" rather than just dimmed. */}
      <div
        className="absolute inset-0 bg-ink-950/55 backdrop-blur-[6px] backdrop-saturate-150"
        onClick={closeOnBackdrop ? onClose : undefined}
        aria-hidden
      />
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={title ? titleId : undefined}
        className={cn(
          "relative w-full bg-white rounded-2xl shadow-pop border border-ink-200/70 overflow-hidden",
          "animate-rise",
          // Cap the dialog to the viewport so tall content scrolls internally
          // (critical on phones / short landscape windows) instead of clipping.
          "flex flex-col max-h-[calc(100dvh-2rem)]",
          // Soft inner highlight on top edge — gives the dialog "lit-from-above" feel
          "before:content-[''] before:absolute before:inset-x-0 before:top-0 before:h-px before:bg-gradient-to-r before:from-transparent before:via-white before:to-transparent",
          sizes[size],
        )}
      >
        {!hideAccentBar && (
          <>
            {/* Soft top accent — keeps modals consistent with toasts/buttons */}
            <div className="absolute inset-x-0 top-0 h-1 bg-gradient-to-r from-brand-500 via-brand-600 to-accent-500" />
            <div className="absolute inset-x-0 top-1 h-px bg-gradient-to-r from-transparent via-white/80 to-transparent" />
          </>
        )}

        {(title || description) && (
          <div className="px-6 pt-7 pb-3 shrink-0">
            {title && <h2 id={titleId} className="text-[18px] font-semibold text-ink-900 leading-tight tracking-tight">{title}</h2>}
            {description && <p className="text-sm text-ink-500 mt-1.5 leading-relaxed">{description}</p>}
          </div>
        )}
        <div className="px-6 pb-6 flex-1 min-h-0 overflow-y-auto">{children}</div>
        {footer && (
          <div className={cn(
            "px-6 py-3.5 border-t hairline shrink-0",
            "bg-gradient-to-b from-ink-50/40 to-ink-50/80",
            "flex flex-wrap justify-end gap-2 items-center",
          )}>
            {footer}
          </div>
        )}
      </div>
    </div>
  );
}
