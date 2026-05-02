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
        setError(err?.data?.detail ?? "This confirmation link is invalid or has expired.");
        setState("fail");
      });
  }, [state, userId, token, confirm]);

  if (state === "loading") {
    return (
      <AuthFrame title="Confirming your email…">
        <div className="flex justify-center"><Spinner size={28} /></div>
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
          <Link to="/login" className="text-brand-600 hover:underline text-sm">Sign in to your account</Link>
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
            If an account exists for <strong>{email}</strong>, a confirmation link has been sent.
          </p>
          <Link to="/login" className="text-brand-600 hover:underline text-sm">Back to sign in</Link>
        </div>
      ) : (
        <form onSubmit={submit} className="space-y-4">
          <Input
            type="email" required label="Email"
            placeholder="you@agency.com"
            value={email} onChange={(e) => setEmail(e.target.value)}
          />
          <Button type="submit" loading={isLoading} fullWidth>Resend confirmation</Button>
          <div className="text-center text-sm">
            <Link to="/login" className="text-brand-600 hover:underline">Back to sign in</Link>
          </div>
        </form>
      )}
    </AuthFrame>
  );
}
