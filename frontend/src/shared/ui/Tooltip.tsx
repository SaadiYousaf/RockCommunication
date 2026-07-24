import { cloneElement, useId, useState, type ReactElement, type ReactNode } from "react";
import { cn } from "./cn";

/**
 * Minimal accessible tooltip. Shows on hover/focus, hides on blur/leave/escape.
 * Adds a subtle pointer arrow toward the trigger so it reads as attached, not
 * floating above unrelated content.
 *
 * <Tooltip content="Saves and starts the cadence">
 *   <Button>Launch</Button>
 * </Tooltip>
 */
export function Tooltip({
  content, children, side = "top", delay = 200, className,
}: {
  content: ReactNode;
  children: ReactElement<any>;
  side?: "top" | "bottom" | "left" | "right";
  /** Hover delay in ms before showing. */
  delay?: number;
  className?: string;
}) {
  const [open, setOpen] = useState(false);
  const id = useId();
  let timer: ReturnType<typeof setTimeout>;

  function show() {
    clearTimeout(timer);
    timer = setTimeout(() => setOpen(true), delay);
  }
  function hide() {
    clearTimeout(timer);
    setOpen(false);
  }

  const sidePos: Record<typeof side, string> = {
    top:    "bottom-full left-1/2 -translate-x-1/2 mb-2",
    bottom: "top-full left-1/2 -translate-x-1/2 mt-2",
    left:   "right-full top-1/2 -translate-y-1/2 mr-2",
    right:  "left-full top-1/2 -translate-y-1/2 ml-2",
  };

  const arrowPos: Record<typeof side, string> = {
    top:    "-bottom-1 left-1/2 -translate-x-1/2",
    bottom: "-top-1 left-1/2 -translate-x-1/2",
    left:   "-right-1 top-1/2 -translate-y-1/2",
    right:  "-left-1 top-1/2 -translate-y-1/2",
  };

  const triggerProps = {
    "aria-describedby": open ? id : undefined,
    onMouseEnter: show,
    onMouseLeave: hide,
    onFocus: show,
    onBlur: hide,
    onKeyDown: (e: React.KeyboardEvent) => { if (e.key === "Escape") hide(); },
  };

  const trigger = cloneElement(children, triggerProps);

  return (
    <span className="relative inline-flex">
      {trigger}
      {open && (
        <span
          id={id}
          role="tooltip"
          className={cn(
            "absolute z-50 pointer-events-none whitespace-nowrap",
            "px-2.5 py-1.5 rounded-lg text-[11.5px] font-medium",
            "bg-ink-900 text-white shadow-pop ring-1 ring-ink-700/40",
            "animate-fade-in",
            sidePos[side],
            className,
          )}
        >
          {content}
          <span
            aria-hidden
            className={cn(
              "absolute w-2 h-2 rotate-45 bg-ink-900 ring-1 ring-ink-700/40",
              arrowPos[side],
            )}
          />
        </span>
      )}
    </span>
  );
}
