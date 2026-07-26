import { getErrorDetail } from "../../shared/api/apiError";
import { useEffect, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { useConfirmEmailMutation, useResendEmailConfirmationMutation } from "../../shared/api/baseApi";
import { Button, Icon, Input, Spinner } from "../../shared/ui";
import { AuthFrame } from "./ForgotPasswordPage";

type State = "loading" | "ok" | "fail" | "missing";

export function ConfirmEmailPage() {
  const [params] = useSearchParams();
  const userId = params.get("userId") ?? "";
  const token = params.get("token") ?? "";

  const [state, setState] = useState<State>(userId && token ? "loading" : "missing");
  const [error, setError] = useState<string | null>(null);
  const [confirm] = useConfirmEmailMutation();
  const ranRef = useRef(false);

  useEffect(() => {
    if (state !== "loading" || ranRef.current) return;
    ranRef.current = true;
    confirm({ userId, token }).unwrap()
      .then(() => setState("ok"))
      .catch((err) => {
        setError(getErrorDetail(err) ?? "This confirmation link is invalid or has expired.");
        setState("fail");
      });
  }, [state, userId, token, confirm]);

  if (state === "loading") {
    return (
      <AuthFrame title="Confirming your email…" subtitle="This only takes a moment.">
        <div className="flex flex-col items-center justify-center gap-3 py-2 text-sm text-ink-500">
          <Spinner size={28} />
          <span>Verifying your confirmation link…</span>
        </div>
      </AuthFrame>
    );
  }

  if (state === "ok") {
    return (
      <AuthFrame title="Email confirmed" subtitle="You're all set.">
        <div className="space-y-4 text-center">
          <div className="mx-auto h-12 w-12 rounded-full bg-success-100 flex items-center justify-center">
            <Icon name="check" size={24} className="text-success-700" />
          </div>
          <Link to="/login" className="inline-flex items-center gap-1.5 text-brand-600 hover:text-brand-700 text-sm font-medium rounded transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500">
            Sign in to your account <Icon name="arrowRight" size={14} />
          </Link>
        </div>
      </AuthFrame>
    );
  }

  if (state === "missing") {
    return <ResendForm reason="This page needs a confirmation link sent to your email." />;
  }

  return <ResendForm reason={error ?? "Link expired or invalid."} />;
}

function ResendForm({ reason }: { reason: string }) {
  const [email, setEmail] = useState("");
  const [sent, setSent] = useState(false);
  const [resend, { isLoading }] = useResendEmailConfirmationMutation();

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    try { await resend({ email }).unwrap(); }
    finally { setSent(true); }
  }

  return (
    <AuthFrame title="Confirmation needed" subtitle={reason}>
      {sent ? (
        <div className="space-y-4 text-center">
          <div className="mx-auto h-12 w-12 rounded-full bg-success-100 flex items-center justify-center">
            <Icon name="check" size={24} className="text-success-700" />
          </div>
          <p className="text-sm text-ink-700">
            If an account exists for <strong className="break-all">{email}</strong>, a confirmation link has been sent.
          </p>
          <Link to="/login" className="inline-flex items-center gap-1.5 text-brand-600 hover:text-brand-700 text-sm font-medium rounded transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500">
            <Icon name="chevronLeft" size={14} /> Back to sign in
          </Link>
        </div>
      ) : (
        <form onSubmit={submit} className="space-y-4">
          <Input
            type="email" required label="Email"
            placeholder="you@agency.com"
            leftIcon={<Icon name="mail" size={16} />}
            value={email} onChange={(e) => setEmail(e.target.value)}
          />
          <Button type="submit" loading={isLoading} fullWidth size="lg" leftIcon={<Icon name="send" size={16} />}>Resend confirmation</Button>
          <div className="text-center text-sm">
            <Link to="/login" className="inline-flex items-center gap-1.5 text-brand-600 hover:text-brand-700 font-medium rounded transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500">
              <Icon name="chevronLeft" size={14} /> Back to sign in
            </Link>
          </div>
        </form>
      )}
    </AuthFrame>
  );
}
