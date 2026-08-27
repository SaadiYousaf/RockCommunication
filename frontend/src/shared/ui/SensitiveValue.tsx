import { useState } from "react";
import { Icon } from "./Icon";
import { cn } from "./cn";
// Imported from the module (not the barrel) to keep this component out of an index.ts import cycle.
import { useToast } from "./Toast";
import { MESSAGES } from "../constants/messages";

/**
 * Displays a sensitive value (SSN, bank account, driver's licence, CNIC, IBAN…) MASKED by default,
 * with an eye toggle to reveal and a one-click copy. Keeps regulated PII off-screen until the
 * operator explicitly reveals it — the standard treatment across the app (mirrors the Confidential
 * vault). Empty values render as an em dash.
 */
export function SensitiveValue({
  value, mono = true, className, maskLength = 10,
}: {
  value?: string | null;
  mono?: boolean;
  className?: string;
  /** How many mask dots to show (kept fixed so the true length isn't leaked). */
  maskLength?: number;
}) {
  const [shown, setShown] = useState(false);
  const [copied, setCopied] = useState(false);
  const toast = useToast();
  const text = value == null ? "" : String(value).trim();
  if (text === "") return <span className="text-ink-400">—</span>;

  async function copy() {
    try {
      await navigator.clipboard.writeText(text);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1200);
    } catch {
      // A blocked clipboard (insecure origin / denied permission) used to fail silently — the tick
      // never appeared and the operator had no idea why. Say so.
      toast.error(MESSAGES.copyFailed, MESSAGES.copyBlocked);
    }
  }

  return (
    <span className={cn("inline-flex items-center gap-1.5", className)}>
      <span className={cn("text-ink-800 select-none", mono && "font-mono tabular-nums")}>
        {shown ? text : "•".repeat(maskLength)}
      </span>
      <button type="button" onClick={() => setShown((s) => !s)}
        aria-label={shown ? "Hide value" : "Reveal value"} title={shown ? "Hide" : "Reveal"}
        className="text-ink-400 hover:text-ink-700 transition-colors">
        <Icon name={shown ? "eyeOff" : "eye"} size={14} />
      </button>
      {shown && (
        <button type="button" onClick={copy} aria-label="Copy value" title={copied ? "Copied" : "Copy"}
          className="text-ink-400 hover:text-ink-700 transition-colors">
          <Icon name={copied ? "check" : "copy"} size={13} />
        </button>
      )}
    </span>
  );
}
