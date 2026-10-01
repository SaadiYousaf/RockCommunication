import type { SyntheticEvent } from "react";
import { useSelector } from "react-redux";
import type { RootState } from "../../app/store";
import { useToast } from "./Toast";

/**
 * Roles whose data entry is copy-restricted on fields marked `secure`.
 *
 * The rule is directional: data may come IN, but it may not go OUT.
 *
 * It used to block paste as well, on the reasoning that hand-keying proves the agent really had the
 * customer on the line. In practice that cost far more than it bought — a closer reading a bank
 * account number back off a recording types it wrong, and a mistyped routing number is a failed
 * draft and a lost policy, which is a worse outcome than the one being prevented. Paste is now
 * allowed.
 *
 * What is actually worth stopping is the other direction. These fields hold social security
 * numbers, driver's licence numbers and bank account details; copying them out of the CRM is how a
 * customer list or a set of banking credentials leaves the building. Copy, cut and drag-out stay
 * blocked.
 *
 * Worth being honest about the limit: this is a deterrent against casual copying, not a security
 * boundary. Anyone can read the screen or photograph it. It raises the effort and makes the intent
 * explicit; it does not make exfiltration impossible.
 */
export const COPY_RESTRICTED_ROLES = ["Fronter", "Verifier", "Closer"];

/** @deprecated Kept so existing imports keep working. Prefer {@link COPY_RESTRICTED_ROLES}. */
export const TYPING_ONLY_ROLES = COPY_RESTRICTED_ROLES;

export interface SecureEntryHandlers {
  onCopy?: (e: SyntheticEvent) => void;
  onCut?: (e: SyntheticEvent) => void;
  onDragStart?: (e: SyntheticEvent) => void;
  onContextMenu?: (e: SyntheticEvent) => void;
}

/**
 * Handlers that stop the contents of a `secure` field being copied out, for roles that are
 * copy-restricted. A no-op for everyone else, so the field behaves normally for unrestricted roles.
 *
 * `onCopy` covers the keyboard shortcut and the browser's own Edit menu alike — both fire the copy
 * event — and `onContextMenu` closes the right-click route to it.
 */
export function useSecureEntry(enabled = true): { restricted: boolean; handlers: SecureEntryHandlers } {
  const roles = useSelector((s: RootState) => s.auth.user?.roles ?? []);
  const toast = useToast();
  const restricted = enabled && roles.some((r) => COPY_RESTRICTED_ROLES.includes(r));

  if (!restricted) return { restricted: false, handlers: {} };

  const block = (e: SyntheticEvent) => {
    e.preventDefault();
    toast.warning(
      "Copying is turned off",
      "These details can't be copied out of the CRM. You can still paste a value in.",
    );
    return false;
  };

  return {
    restricted: true,
    // Paste and drop are deliberately absent — getting a value INTO the field is allowed.
    handlers: { onCopy: block, onCut: block, onDragStart: block, onContextMenu: block },
  };
}
