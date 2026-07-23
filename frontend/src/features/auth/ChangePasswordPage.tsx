import { getErrorDetail } from "../../shared/api/apiError";
import { useState } from "react";
import { useDispatch, useSelector } from "react-redux";
import { useNavigate } from "react-router-dom";
import { useChangePasswordMutation, useMeQuery } from "../../shared/api/baseApi";
import { setAuth } from "../../app/store";
import type { RootState } from "../../app/store";
import { Button, Card, CardBody, Icon, Input, useToast } from "../../shared/ui";
import { BrandLogo } from "../../shared/components/BrandLogo";

/**
 * Forced first-login password change.
 *
 * Lands here automatically when /api/auth/me returns `mustChangePassword: true`
 * (set by the backend for any account created with an admin-supplied or auto-generated
 * temporary password). The page is not gated by ProtectedRoute → it sits at /change-password
 * and is the only place a `mustChangePassword` user can be.
 */
export function ChangePasswordPage() {
  const auth = useSelector((s: RootState) => s.auth);
  const dispatch = useDispatch();
  const navigate = useNavigate();
  const toast = useToast();
  const { refetch: refetchMe } = useMeQuery(undefined, { skip: !auth.accessToken });
  const [changePassword, { isLoading }] = useChangePasswordMutation();

  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [confirm, setConfirm] = useState("");
  const [showPwd, setShowPwd] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const matches = next.length > 0 && next === confirm;
  const meets = next.length >= 8
    && /[A-Z]/.test(next) && /[a-z]/.test(next)
    && /\d/.test(next) && /[^A-Za-z0-9]/.test(next);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!meets) return setError("Password must be ≥8 chars and include upper, lower, digit and symbol.");
    if (!matches) return setError("New passwords do not match.");
    if (next === current) return setError("Pick a password different from the temporary one.");

    try {
      await changePassword({ currentPassword: current, newPassword: next }).unwrap();
      toast.success("Password updated", "You're all set.");

      // Backend revokes refresh tokens on password change. Refetch /me to get
      // the cleared mustChangePassword flag, then send the user to the dashboard.
      try {
        const me = await refetchMe().unwrap();
        if (auth.accessToken && auth.refreshToken && me) {
          dispatch(setAuth({ accessToken: auth.accessToken, refreshToken: auth.refreshToken, user: me }));
        }
      } catch { /* if /me fails the user can just sign in again */ }
      navigate("/dashboard");
    } catch (err: unknown) {
      const msg = getErrorDetail(err) ?? getErrorDetail(err) ?? "Couldn't change password.";
      setError(msg);
      toast.error("Couldn't change password", msg);
    }
  }

  const isForced = !!auth.user?.mustChangePassword;

  return (
    <div className="min-h-screen grid place-items-center p-6 bg-ink-50">
      <Card elevated className="max-w-md w-full">
        <CardBody>
          <div className="flex items-center gap-3 mb-5">
            <BrandLogo variant="mark" size={36} />
            <div className="leading-tight">
              <div className="text-sm font-semibold text-ink-900">Rock Communication</div>
              <div className="text-[10px] uppercase tracking-[0.18em] text-ink-500">Insurance Agency</div>
            </div>
          </div>

          <h1 className="text-xl font-semibold text-ink-900">
            {isForced ? "Set your password" : "Change your password"}
          </h1>
          <p className="text-sm text-ink-500 mt-1.5">
            {isForced
              ? "You're using a temporary password. Choose a new one to continue."
              : "Pick a new password. You'll be signed out from other devices."}
          </p>

          <form onSubmit={submit} className="space-y-4 mt-6">
            <Input
              label={isForced ? "Temporary password" : "Current password"}
              type={showPwd ? "text" : "password"}
              value={current}
              onChange={(e) => setCurrent(e.target.value)}
              leftIcon={<Icon name="shield" size={16} />}
              autoComplete="current-password"
              required autoFocus
            />
            <Input
              label="New password"
              type={showPwd ? "text" : "password"}
              value={next}
              onChange={(e) => setNext(e.target.value)}
              leftIcon={<Icon name="shield" size={16} />}
              hint="≥ 8 chars · upper, lower, digit, symbol"
              autoComplete="new-password"
              required
            />
            <Input
              label="Confirm new password"
              type={showPwd ? "text" : "password"}
              value={confirm}
              onChange={(e) => setConfirm(e.target.value)}
              leftIcon={<Icon name="shield" size={16} />}
              error={confirm && !matches ? "Passwords do not match." : undefined}
              autoComplete="new-password"
              required
            />

            <label className="inline-flex items-center gap-2 text-xs text-ink-600">
              <input
                type="checkbox"
                checked={showPwd}
                onChange={(e) => setShowPwd(e.target.checked)}
                className="rounded border-ink-300 text-brand-600 focus:ring-brand-500"
              />
              Show passwords
            </label>

            {error && <div className="text-sm text-rose-600">{error}</div>}

            <Button type="submit" loading={isLoading} fullWidth size="lg">
              {isForced ? "Set new password & continue" : "Update password"}
            </Button>
          </form>
        </CardBody>
      </Card>
    </div>
  );
}
