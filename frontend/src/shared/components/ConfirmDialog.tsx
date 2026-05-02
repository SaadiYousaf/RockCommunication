import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from "react";
import { Button, Modal } from "../ui";

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

  const confirm = useCallback<ConfirmFn>((opts) => {
    return new Promise<boolean>((resolve) => {
      setPending({ ...opts, resolve });
    });
  }, []);

  const close = (ok: boolean) => {
    if (!pending) return;
    pending.resolve(ok);
    setPending(null);
  };

  const value = useMemo(() => confirm, [confirm]);

  return (
    <Ctx.Provider value={value}>
      {children}
      <Modal
        open={!!pending}
        onClose={() => close(false)}
        title={pending?.title}
        size="sm"
      >
        {pending?.description && (
          <p className="text-sm text-ink-700 leading-relaxed">{pending.description}</p>
        )}
        <div className="flex justify-end gap-2 mt-6">
          <Button variant="ghost" onClick={() => close(false)}>
            {pending?.cancelLabel ?? "Cancel"}
          </Button>
          <Button
            variant={pending?.danger ? "danger" : "primary"}
            onClick={() => close(true)}
            autoFocus
          >
            {pending?.confirmLabel ?? "Confirm"}
          </Button>
        </div>
      </Modal>
    </Ctx.Provider>
  );
}
