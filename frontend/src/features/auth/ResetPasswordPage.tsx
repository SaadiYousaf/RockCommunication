import { getErrorDetail } from "../../shared/api/apiError";
import { useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { useResetPasswordMutation } from "../../shared/api/baseApi";
import { Button, Input, useToast } from "../../shared/ui";
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
        <Link to="/forgot-password" className="text-brand-600 hover:underline text-sm block text-center">
          Request a new link
        </Link>
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
          value={pwd} onChange={(e) => setPwd(e.target.value)}
          rightSlot={
            <button type="button" onClick={() => setShow((s) => !s)} className="text-ink-400 hover:text-ink-600 text-xs px-2">
              {show ? "Hide" : "Show"}
            </button>
          }
        />
        <Input
          type={show ? "text" : "password"} required label="Confirm password"
          placeholder="Repeat password"
          value={confirm} onChange={(e) => setConfirm(e.target.value)}
        />
        {error && <div className="text-sm text-danger-700 bg-danger-50 border border-danger-200 rounded p-2">{error}</div>}
        <Button type="submit" loading={isLoading} fullWidth size="lg">Reset password</Button>
        <div className="text-center text-sm">
          <Link to="/login" className="text-brand-600 hover:underline">Back to sign in</Link>
        </div>
      </form>
    </AuthFrame>
  );
}
