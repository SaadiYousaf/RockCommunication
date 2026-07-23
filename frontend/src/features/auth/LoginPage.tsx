import { getErrorDetail } from "../../shared/api/apiError";
import { useState } from "react";
import { useDispatch } from "react-redux";
import { useNavigate } from "react-router-dom";
import { markSessionRecovered, useLoginMutation, useVerify2FaMutation } from "../../shared/api/baseApi";
import { setAuth } from "../../app/store";
import { Button, Icon, Input, useToast } from "../../shared/ui";
import { BrandLogo } from "../../shared/components/BrandLogo";

export function LoginPage() {
  const [userNameOrEmail, setU] = useState("admin");
  const [password, setP] = useState("");
  const [code, setCode] = useState("");
  const [twoFactorToken, setTfToken] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [showPwd, setShowPwd] = useState(false);

  const [login, { isLoading: loggingIn }] = useLoginMutation();
  const [verify2fa, { isLoading: verifying }] = useVerify2FaMutation();
  const dispatch = useDispatch();
  const navigate = useNavigate();
  const toast = useToast();

  async function handleLogin(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    try {
      const result = await login({ userNameOrEmail, password }).unwrap();
      if (result.requiresTwoFactor && result.twoFactorToken) {
        setTfToken(result.twoFactorToken);
        toast.info("Two-factor required", "Enter the code from your authenticator app.");
      } else if (result.user) {
        markSessionRecovered();
        dispatch(setAuth({ accessToken: result.accessToken, refreshToken: result.refreshToken, user: result.user }));
        if (result.user.mustChangePassword) {
          toast.info("Set your password", "You're using a temporary password — pick a new one to continue.");
          navigate("/change-password");
        } else {
          toast.success("Welcome back", `Signed in as ${result.user.userName}`);
          navigate("/dashboard");
        }
      }
    } catch (err: unknown) {
      const msg = getErrorDetail(err) ?? "Login failed.";
      setError(msg);
      toast.error("Sign in failed", msg);
    }
  }

  async function handleVerify(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!twoFactorToken) return;
    try {
      const result = await verify2fa({ twoFactorToken, code }).unwrap();
      if (result.user) {
        markSessionRecovered();
        dispatch(setAuth({ accessToken: result.accessToken, refreshToken: result.refreshToken, user: result.user }));
        if (result.user.mustChangePassword) {
          navigate("/change-password");
        } else {
          toast.success("Verified", "Two-factor authentication successful.");
          navigate("/dashboard");
        }
      }
    } catch (err: unknown) {
      const msg = getErrorDetail(err) ?? "Verification failed.";
      setError(msg);
      toast.error("Verification failed", msg);
    }
  }

  return (
    <div className="min-h-screen grid lg:grid-cols-2 bg-ink-50">
      {/* Left brand panel */}
      <div className="hidden lg:flex relative overflow-hidden bg-gradient-to-br from-[#2a2f38] via-[#1d232c] to-[#0a1730] text-white">
        {/* glow accents echo the logo's blue rim */}
        <div className="absolute -top-32 -left-32 h-96 w-96 rounded-full bg-brand-500/30 blur-3xl" />
        <div className="absolute -bottom-32 -right-24 h-[420px] w-[420px] rounded-full bg-brand-400/20 blur-3xl" />
        <div className="absolute inset-0 bg-grid opacity-40" />

        <div className="relative z-10 flex flex-col justify-between p-12 w-full">
          <div className="flex items-center gap-3">
            <BrandLogo variant="mark" size={44} className="drop-shadow-[0_4px_18px_rgba(31,126,255,0.5)]" />
            <div>
              <div className="text-base font-semibold tracking-tight">Rock Communication</div>
              <div className="text-[10px] uppercase tracking-[0.18em] text-white/70">Insurance Agency</div>
            </div>
          </div>

          {/* Hero logo + tagline */}
          <div className="flex flex-col items-start max-w-md">
            <BrandLogo variant="full" className="w-72 mb-8 drop-shadow-[0_8px_32px_rgba(31,126,255,0.35)]" />
            <h1 className="text-3xl font-semibold leading-tight">
              Built on a rock-solid foundation for insurance teams.
            </h1>
            <p className="text-white/75 mt-3">
              Pipelines, dialer, callbacks, commissions and compliance — one workspace for the whole agency.
            </p>
            <div className="grid grid-cols-3 gap-3 pt-8 w-full">
              {[
                { v: "99.9%", l: "Uptime" },
                { v: "SOC 2", l: "Compliant" },
                { v: "24/7",  l: "Support" },
              ].map((s) => (
                <div key={s.l} className="bg-white/5 ring-1 ring-white/10 rounded-xl p-3 backdrop-blur">
                  <div className="text-lg font-semibold">{s.v}</div>
                  <div className="text-[11px] uppercase tracking-wider text-white/70">{s.l}</div>
                </div>
              ))}
            </div>
          </div>

          <div className="text-xs text-white/60">© {new Date().getFullYear()} Rock Communication Insurance Agency. All rights reserved.</div>
        </div>
      </div>

      {/* Right form panel */}
      <div className="flex items-center justify-center p-6 sm:p-12">
        <div className="w-full max-w-sm">
          <div className="lg:hidden flex items-center gap-2.5 mb-8">
            <BrandLogo variant="mark" size={36} />
            <div className="leading-tight">
              <div className="text-sm font-semibold">Rock Communication</div>
              <div className="text-[10px] uppercase tracking-[0.18em] text-ink-500">Insurance Agency</div>
            </div>
          </div>

          {!twoFactorToken && (
            <>
              <h2 className="text-2xl font-semibold text-ink-900">Sign in to your workspace</h2>
              <p className="text-sm text-ink-500 mt-1.5 mb-8">
                Welcome back. Enter your credentials to continue.
              </p>

              <form onSubmit={handleLogin} className="space-y-4">
                <Input
                  label="Username or email"
                  placeholder="you@company.com"
                  value={userNameOrEmail}
                  onChange={(e) => setU(e.target.value)}
                  leftIcon={<Icon name="users" size={16} />}
                  required autoFocus
                />
                <Input
                  label="Password"
                  type={showPwd ? "text" : "password"}
                  placeholder="••••••••"
                  value={password}
                  onChange={(e) => setP(e.target.value)}
                  leftIcon={<Icon name="shield" size={16} />}
                  rightSlot={
                    <button
                      type="button"
                      onClick={() => setShowPwd((s) => !s)}
                      className="text-xs text-ink-500 hover:text-ink-800 px-2"
                    >{showPwd ? "Hide" : "Show"}</button>
                  }
                  error={error ?? undefined}
                  required
                />

                <div className="flex items-center justify-between text-xs">
                  <label className="inline-flex items-center gap-2 text-ink-600">
                    <input type="checkbox" className="rounded border-ink-300 text-brand-600 focus:ring-brand-500" />
                    Remember me
                  </label>
                  <a className="text-brand-600 hover:text-brand-700 font-medium" href="/forgot-password">Forgot password?</a>
                </div>

                <Button type="submit" loading={loggingIn} fullWidth size="lg">
                  Sign in
                </Button>
              </form>
            </>
          )}

          {twoFactorToken && (
            <>
              <div className="h-12 w-12 rounded-xl bg-brand-50 text-brand-600 grid place-items-center mb-4">
                <Icon name="shield" size={22} />
              </div>
              <h2 className="text-2xl font-semibold text-ink-900">Two-factor authentication</h2>
              <p className="text-sm text-ink-500 mt-1.5 mb-8">
                Enter the 6-digit code from your authenticator app.
              </p>

              <form onSubmit={handleVerify} className="space-y-4">
                <Input
                  label="Verification code"
                  inputMode="numeric"
                  maxLength={6}
                  placeholder="000000"
                  value={code}
                  onChange={(e) => setCode(e.target.value.replace(/\D/g, ""))}
                  className="tracking-[0.5em] text-center text-lg font-semibold"
                  error={error ?? undefined}
                  required autoFocus
                />
                <Button type="submit" loading={verifying} fullWidth size="lg">
                  Verify and continue
                </Button>
                <Button
                  type="button" variant="ghost" fullWidth
                  onClick={() => { setTfToken(null); setCode(""); setError(null); }}
                >
                  Back to sign in
                </Button>
              </form>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
