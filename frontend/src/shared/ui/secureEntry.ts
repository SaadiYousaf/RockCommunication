import type { SyntheticEvent } from "react";
import { useSelector } from "react-redux";
import type { RootState } from "../../app/store";
import { useToast } from "./Toast";

/**
 * Roles that are restricted to typing-only data entry. For these roles, copy /
 * paste / cut / drag on a field marked `secure` is blocked so data must be
 * hand-keyed (fraud / data-integrity control). Mirrors the backend role names.
 */
export const TYPING_ONLY_ROLES = ["Fronter", "Verifier", "Closer"];

export interface SecureEntryHandlers {
  onPaste?: (e: SyntheticEvent) => void;
  onCopy?: (e: SyntheticEvent) => void;
  onCut?: (e: SyntheticEvent) => void;
  onDrop?: (e: SyntheticEvent) => void;
  onDragStart?: (e: SyntheticEvent) => void;
}

/**
 * Returns event handlers that block copy/paste when the signed-in user holds a
 * typing-only role. Returns an empty object (no-op) for everyone else, so a
 * `secure` field behaves normally for unrestricted roles.
 */
export function useSecureEntry(enabled = true): { restricted: boolean; handlers: SecureEntryHandlers } {
  const roles = useSelector((s: RootState) => s.auth.user?.roles ?? []);
  const toast = useToast();
  const restricted = enabled && roles.some((r) => TYPING_ONLY_ROLES.includes(r));

  if (!restricted) return { restricted: false, handlers: {} };

  const block = (e: SyntheticEvent) => {
    e.preventDefault();
    toast.warning("Typing only", "Copy / paste is disabled here — please type the value in.");
    return false;
  };

  return {
    restricted: true,
    handlers: { onPaste: block, onCopy: block, onCut: block, onDrop: block, onDragStart: block },
  };
}
