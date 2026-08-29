import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from "react";
import { Button, Icon, Input, Modal } from "../ui";

/**
 * `useConfirm()` — promise-based confirmation dialog.
 *
 *   const confirm = useConfirm();
 *   if (await confirm({ title: "Delete role?", danger: true })) { ... }
 *
 * Replaces ad-hoc `<Modal open={confirmDelete}>` blocks scattered across pages.
 * Mounts a single shared modal at the layout level.
 */

interface ConfirmOptions {
  title: string;
  description?: ReactNode;
  confirmLabel?: string;
  cancelLabel?: string;
  /** When true the confirm button uses the danger variant. */
  danger?: boolean;
  /**
   * What will actually happen, one line each. "Are you sure?" tells nobody anything — a person about
   * to shut down a tenant needs to read that its call centres go down and its staff get signed out
   * BEFORE they press the button, not discover it afterwards.
   */
  consequences?: ReactNode[];
  /**
   * Require the operator to type this exact text to enable the confirm button. Reserve it for
   * actions whose blast radius is a whole tenant — friction everywhere trains people to ignore it.
   */
  requireTypeToConfirm?: string;
  /** Label shown above the type-to-confirm field, e.g. "Type the agency name to confirm". */
  typeToConfirmLabel?: string;
}

type ConfirmFn = (opts: ConfirmOptions) => Promise<boolean>;

const Ctx = createContext<ConfirmFn | null>(null);

export function useConfirm(): ConfirmFn {
  const fn = useContext(Ctx);
  if (!fn) throw new Error("useConfirm must be used inside <ConfirmProvider>");
  return fn;
}

interface PendingConfirm extends ConfirmOptions {
  resolve: (ok: boolean) => void;
}

export function ConfirmProvider({ children }: { children: ReactNode }) {
  const [pending, setPending] = useState<PendingConfirm | null>(null);
  const [typed, setTyped] = useState("");

  const confirm = useCallback<ConfirmFn>((opts) => {
    return new Promise<boolean>((resolve) => {
      setTyped("");
      setPending({ ...opts, resolve });
    });
  }, []);

  const close = (ok: boolean) => {
    if (!pending) return;
    pending.resolve(ok);
    setPending(null);
    setTyped("");
  };

  // Compared case-insensitively and trimmed: this is a speed bump to make the operator read what
  // they are about to do, not a spelling test.
  const canConfirm = !pending?.requireTypeToConfirm
    || typed.trim().toLowerCase() === pending.requireTypeToConfirm.trim().toLowerCase();

  const value = useMemo(() => confirm, [confirm]);

  return (
    <Ctx.Provider value={value}>
      {children}
      <Modal
        open={!!pending}
        onClose={() => close(false)}
        title={pending?.title}
        size={pending?.consequences?.length || pending?.requireTypeToConfirm ? "md" : "sm"}
      >
        {/* A div, not a <p>: the description may carry rich content, and a list inside a paragraph
            is invalid HTML that browsers silently break apart. */}
        {pending?.description && (
          <div className="text-sm text-ink-700 leading-relaxed">{pending.description}</div>
        )}

        {!!pending?.consequences?.length && (
          <ul className="mt-3 space-y-2 rounded-xl bg-ink-50 border border-ink-100 px-4 py-3">
            {pending.consequences.map((c, i) => (
              <li key={i} className="flex items-start gap-2 text-sm text-ink-700 leading-relaxed">
                <Icon name="chevronRight" size={13} className="mt-1 shrink-0 text-ink-400" />
                <span>{c}</span>
              </li>
            ))}
          </ul>
        )}

        {pending?.requireTypeToConfirm && (
          <div className="mt-4">
            <Input
              label={pending.typeToConfirmLabel ?? `Type “${pending.requireTypeToConfirm}” to confirm`}
              value={typed}
              onChange={(e) => setTyped(e.target.value)}
              autoFocus
            />
          </div>
        )}

        <div className="flex justify-end gap-2 mt-6">
          <Button variant="ghost" onClick={() => close(false)}>
            {pending?.cancelLabel ?? "Cancel"}
          </Button>
          <Button
            variant={pending?.danger ? "danger" : "primary"}
            onClick={() => close(true)}
            disabled={!canConfirm}
            autoFocus={!pending?.requireTypeToConfirm}
          >
            {pending?.confirmLabel ?? "Confirm"}
          </Button>
        </div>
      </Modal>
    </Ctx.Provider>
  );
}
