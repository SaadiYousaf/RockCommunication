import { getErrorDetail } from "../../shared/api/apiError";
import { useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { useResetPasswordMutation } from "../../shared/api/baseApi";
import { Button, Icon, Input, useToast } from "../../shared/ui";
import { AuthFrame } from "./ForgotPasswordPage";

export function ResetPasswordPage() {
  const [params] = useSearchParams();
  const email = params.get("email") ?? "";
  const token = params.get("token") ?? "";
  const navigate = useNavigate();
  const toast = useToast();

  const [pwd, setPwd] = useState("");
  const [confirm, setConfirm] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [show, setShow] = useState(false);

  const [reset, { isLoading }] = useResetPasswordMutation();

  if (!email || !token) {
    return (
      <AuthFrame title="Invalid reset link" subtitle="This link is missing required information.">
        <div className="space-y-4 text-center">
          <div className="mx-auto h-12 w-12 rounded-full bg-danger-100 flex items-center justify-center">
            <Icon name="alert" size={24} className="text-danger-700" />
          </div>
          <Link to="/forgot-password" className="inline-flex items-center gap-1.5 text-brand-600 hover:text-brand-700 text-sm font-medium rounded transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500">
            <Icon name="refresh" size={14} /> Request a new link
          </Link>
        </div>
      </AuthFrame>
    );
  }

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (pwd.length < 8) return setError("Password must be at least 8 characters.");
    if (pwd !== confirm) return setError("Passwords don't match.");
    try {
      await reset({ email, token, newPassword: pwd }).unwrap();
      toast.success("Password reset", "You can sign in with your new password.");
      navigate("/login", { replace: true });
    } catch (err: unknown) {
      setError(getErrorDetail(err) ?? "This reset link is invalid or has expired.");
    }
  }

  return (
    <AuthFrame title="Choose a new password" subtitle={`Resetting password for ${email}`}>
      <form onSubmit={submit} className="space-y-4">
        <Input
          type={show ? "text" : "password"} required label="New password"
          placeholder="At least 8 characters"
          leftIcon={<Icon name="shield" size={16} />}
          value={pwd} onChange={(e) => setPwd(e.target.value)}
          autoComplete="new-password"
          rightSlot={
            <button type="button" onClick={() => setShow((s) => !s)} className="text-ink-400 hover:text-ink-600 text-xs px-2 rounded transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500">
              {show ? "Hide" : "Show"}
            </button>
          }
        />
        <Input
          type={show ? "text" : "password"} required label="Confirm password"
          placeholder="Repeat password"
          leftIcon={<Icon name="shield" size={16} />}
          value={confirm} onChange={(e) => setConfirm(e.target.value)}
          autoComplete="new-password"
        />
        {error && (
          <div className="flex items-start gap-2 text-sm text-danger-700 bg-danger-50 border border-danger-200 rounded-lg p-2.5">
            <Icon name="warning" size={16} className="mt-0.5 shrink-0 text-danger-500" />
            <span>{error}</span>
          </div>
        )}
        <Button type="submit" loading={isLoading} fullWidth size="lg">Reset password</Button>
        <div className="text-center text-sm">
          <Link to="/login" className="inline-flex items-center gap-1.5 text-brand-600 hover:text-brand-700 font-medium rounded transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500">
            <Icon name="chevronLeft" size={14} /> Back to sign in
          </Link>
        </div>
      </form>
    </AuthFrame>
  );
}
