import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from "react";
import { useDispatch } from "react-redux";
import { useNavigate } from "react-router-dom";
import { Button, Icon, Modal, Textarea, cn, useToast } from "../ui";
import { clearAuth } from "../../app/authSlice";
import { useSubmitReflectionMutation } from "../../features/hr/reflectionsApi";
import { SIGN_OUT_MSG } from "../constants/messages";

/**
 * `useSignOut()` — ends the session, asking two end-of-day questions on the way out.
 *
 *   const signOut = useSignOut();
 *   <button onClick={signOut}>Sign out</button>
 *
 * Mounted once at the layout level so every deliberate sign-out in the app goes through the same
 * dialog. It is wired ONLY to the places a person chooses to leave — the user menu and the command
 * palette. An involuntary sign-out (expired refresh token, deactivated account, a 401 from the
 * refresh endpoint) still calls `clearAuth` directly and must never raise this dialog: there is
 * nobody at the keyboard at 3am, and a questionnaire between a dead session and the login screen
 * would be a trap rather than a prompt.
 *
 * Answering is optional end to end — Skip signs out without sending anything, and a save that
 * fails still signs the person out. The session is theirs to end; this is a question, not a gate.
 */
type SignOutFn = () => void;

const Ctx = createContext<SignOutFn | null>(null);

export function useSignOut(): SignOutFn {
  const fn = useContext(Ctx);
  if (!fn) throw new Error("useSignOut must be used inside <SignOutProvider>");
  return fn;
}

export function SignOutProvider({ children }: { children: ReactNode }) {
  const dispatch = useDispatch();
  const navigate = useNavigate();
  const toast = useToast();
  const [submitReflection] = useSubmitReflectionMutation();

  const [open, setOpen] = useState(false);
  const [learned, setLearned] = useState("");
  const [didJustice, setDidJustice] = useState<boolean | null>(null);
  const [note, setNote] = useState("");
  const [busy, setBusy] = useState(false);

  const requestSignOut = useCallback<SignOutFn>(() => {
    setLearned("");
    setDidJustice(null);
    setNote("");
    setBusy(false);
    setOpen(true);
  }, []);

  /** The actual sign-out. Identical to what the user menu did before this dialog existed. */
  const endSession = useCallback(() => {
    setOpen(false);
    setBusy(false);
    dispatch(clearAuth());
    navigate("/login");
  }, [dispatch, navigate]);

  const hasAnswer = learned.trim().length > 0 || didJustice !== null || note.trim().length > 0;

  const submitAndSignOut = async () => {
    // Nothing filled in is the same thing as skipping. Sending an empty body would be rejected by
    // the validator and show an error for doing nothing wrong.
    if (!hasAnswer) { endSession(); return; }

    setBusy(true);
    try {
      await submitReflection({
        learned: learned.trim() || null,
        didJustice,
        justiceNote: note.trim() || null,
      }).unwrap();
      toast.success(SIGN_OUT_MSG.saved);
    } catch {
      // Deliberately swallowed. The person asked to leave; losing one day's note is a far smaller
      // failure than leaving them stuck on a dialog they cannot get past.
      toast.error(SIGN_OUT_MSG.saveFailed);
    }
    endSession();
  };

  const value = useMemo(() => requestSignOut, [requestSignOut]);

  return (
    <Ctx.Provider value={value}>
      {children}
      <Modal
        open={open}
        // Dismissing the dialog cancels the sign-out and leaves them signed in. Leaving is done
        // through one of the two buttons, both of which say that they sign out.
        onClose={() => { if (!busy) setOpen(false); }}
        title={SIGN_OUT_MSG.title}
        size="md"
      >
        <p className="text-sm text-ink-600">{SIGN_OUT_MSG.description}</p>

        <div className="mt-5 space-y-5">
          <Textarea
            label={SIGN_OUT_MSG.learnedLabel}
            placeholder={SIGN_OUT_MSG.learnedPlaceholder}
            value={learned}
            onChange={(e) => setLearned(e.target.value)}
            rows={3}
            maxLength={4000}
            autoFocus
          />

          <div>
            <div className="text-sm font-medium text-ink-800">{SIGN_OUT_MSG.justiceLabel}</div>
            <div className="mt-2 flex gap-2">
              <YesNo
                selected={didJustice === true}
                label={SIGN_OUT_MSG.justiceYes}
                icon="check"
                tone="yes"
                // Clicking the chosen answer again clears it — the way back to not having answered.
                onClick={() => setDidJustice(didJustice === true ? null : true)}
              />
              <YesNo
                selected={didJustice === false}
                label={SIGN_OUT_MSG.justiceNo}
                icon="x"
                tone="no"
                onClick={() => setDidJustice(didJustice === false ? null : false)}
              />
            </div>
            {/* Said before they type, not after: somebody answering "no" should know their manager
                will read it, so they can decide how much to write. */}
            {didJustice === false && (
              <p className="mt-2 text-xs text-ink-500">{SIGN_OUT_MSG.justiceNoHint}</p>
            )}
          </div>

          <Textarea
            label={SIGN_OUT_MSG.justiceNoteLabel}
            placeholder={SIGN_OUT_MSG.justiceNotePlaceholder}
            value={note}
            onChange={(e) => setNote(e.target.value)}
            rows={2}
            maxLength={2000}
          />
        </div>

        <div className="flex justify-end gap-2 mt-6">
          <Button variant="ghost" onClick={endSession} disabled={busy}>
            {SIGN_OUT_MSG.skip}
          </Button>
          <Button variant="primary" onClick={submitAndSignOut} disabled={busy}>
            {busy ? SIGN_OUT_MSG.signingOut : SIGN_OUT_MSG.submit}
          </Button>
        </div>
      </Modal>
    </Ctx.Provider>
  );
}

/** A yes/no answer chip. Two of them, so the answer is one tap rather than a dropdown. */
function YesNo({
  selected, label, icon, tone, onClick,
}: {
  selected: boolean;
  label: string;
  icon: "check" | "x";
  tone: "yes" | "no";
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      aria-pressed={selected}
      onClick={onClick}
      className={cn(
        "inline-flex items-center gap-2 rounded-xl border px-4 py-2 text-sm font-medium transition-colors",
        "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500/40",
        !selected && "border-ink-200 text-ink-600 hover:border-ink-300 hover:bg-ink-50",
        selected && tone === "yes" && "border-emerald-300 bg-emerald-50 text-emerald-800",
        selected && tone === "no" && "border-amber-300 bg-amber-50 text-amber-800",
      )}
    >
      <Icon name={icon} size={15} />
      {label}
    </button>
  );
}
