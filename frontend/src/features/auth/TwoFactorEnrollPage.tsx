import { useEffect, useState } from "react";
import QRCode from "qrcode";
import {
  useEnable2FaMutation, useSendEmailOtpMutation, useSetTwoFactorMethodMutation, useSetup2FaMutation,
  useDisable2FaMutation, useGet2FaStatusQuery,
} from "../../shared/api/baseApi";

type Method = "Totp" | "EmailOtp";

export function TwoFactorEnrollPage() {
  const { data: status, refetch: refetchStatus, isLoading: loadingStatus } = useGet2FaStatusQuery();
  const [method, setMethod] = useState<Method>("Totp");
  const [step, setStep] = useState<"choose" | "setup" | "verify" | "done">("choose");
  const [setup, { data: totpData, isLoading: settingUp }] = useSetup2FaMutation();
  const [enable, { isLoading: enabling }] = useEnable2FaMutation();
  const [setMethodApi] = useSetTwoFactorMethodMutation();
  const [sendEmailOtp, { isLoading: sendingOtp }] = useSendEmailOtpMutation();
  const [disable, { isLoading: disabling }] = useDisable2FaMutation();
  const [disableConfirm, setDisableConfirm] = useState(false);

  const [code, setCode] = useState("");
  const [qrDataUrl, setQrDataUrl] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [secretCopied, setSecretCopied] = useState(false);

  useEffect(() => {
    if (!totpData?.qrCodeUri) { setQrDataUrl(null); return; }
    QRCode.toDataURL(totpData.qrCodeUri, { width: 240, margin: 2 })
      .then(setQrDataUrl)
      .catch(() => setQrDataUrl(null));
  }, [totpData?.qrCodeUri]);

  async function chooseMethod(m: Method) {
    setMethod(m);
    setError(null);
    setCode("");
    try {
      await setMethodApi({ method: m }).unwrap();
      if (m === "Totp") {
        await setup().unwrap();
        setStep("setup");
      } else {
        await sendEmailOtp().unwrap();
        setStep("verify");
      }
    } catch (err: any) {
      setError(err?.data?.detail ?? "Couldn't start setup. Try again.");
    }
  }

  async function verify(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    try {
      await enable({ code }).unwrap();
      setStep("done");
      refetchStatus();
    } catch (err: any) {
      setError(err?.data?.detail ?? "Invalid code. Try again.");
    }
  }

  async function disable2Fa() {
    setError(null);
    try {
      await disable().unwrap();
      setDisableConfirm(false);
      setStep("choose");
      setCode("");
      refetchStatus();
    } catch (err: any) {
      setError(err?.data?.detail ?? "Couldn't disable 2FA. Try again.");
    }
  }

  async function copySecret() {
    if (!totpData?.secret) return;
    try {
      await navigator.clipboard.writeText(totpData.secret);
      setSecretCopied(true);
      setTimeout(() => setSecretCopied(false), 1800);
    } catch {}
  }

  return (
    <div className="max-w-2xl">
      <h1 className="text-2xl font-semibold mb-1">Two-factor authentication</h1>
      <p className="text-sm text-slate-600 mb-6">
        Add a second step at sign-in to protect your account, even if your password is stolen.
      </p>

      {!loadingStatus && status?.enabled && step !== "done" && (
        <div className="bg-emerald-50 border border-emerald-200 rounded-lg p-5 mb-6">
          <div className="flex items-start gap-3">
            <div className="text-2xl">🔒</div>
            <div className="flex-1">
              <div className="text-emerald-900 font-semibold">
                Two-factor authentication is on
              </div>
              <p className="text-sm text-emerald-800 mt-1">
                Method: <span className="font-mono">{status.method ?? "Authenticator app"}</span>.
                You're prompted for a code at every sign-in.
              </p>
              {!disableConfirm ? (
                <button
                  className="mt-3 text-sm bg-white border border-rose-300 text-rose-700 hover:bg-rose-50 px-3 py-1.5 rounded"
                  onClick={() => setDisableConfirm(true)}>
                  Turn off 2FA
                </button>
              ) : (
                <div className="mt-3 flex flex-wrap gap-2 items-center">
                  <span className="text-sm text-rose-800">
                    Are you sure? Your account will only be protected by your password.
                  </span>
                  <button
                    className="text-sm bg-rose-600 hover:bg-rose-700 text-white px-3 py-1.5 rounded disabled:opacity-50"
                    disabled={disabling}
                    onClick={disable2Fa}>
                    {disabling ? "Disabling…" : "Yes, turn off"}
                  </button>
                  <button
                    className="text-sm bg-white border border-slate-300 text-slate-700 hover:bg-slate-50 px-3 py-1.5 rounded"
                    onClick={() => setDisableConfirm(false)}>
                    Cancel
                  </button>
                </div>
              )}
              {error && <div className="text-sm text-rose-700 mt-2">{error}</div>}
            </div>
          </div>
        </div>
      )}

      {!status?.enabled && <Steps current={step} />}

      {!status?.enabled && step === "choose" && (
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4 mt-6">
          <MethodCard
            icon="📱"
            title="Authenticator app"
            description="Use Google Authenticator, Authy, 1Password, etc. Most secure."
            recommended
            onPick={() => chooseMethod("Totp")}
            disabled={settingUp}
          />
          <MethodCard
            icon="✉️"
            title="Email code"
            description="Receive a 6-digit code on every sign-in by email."
            onPick={() => chooseMethod("EmailOtp")}
            disabled={sendingOtp}
          />
        </div>
      )}

      {!status?.enabled && step === "setup" && method === "Totp" && totpData && (
        <div className="bg-white border border-slate-200 rounded-lg p-6 mt-6 space-y-5">
          <div>
            <div className="text-sm font-semibold mb-1">Step 1 — scan with your authenticator</div>
            <p className="text-sm text-slate-600 mb-3">
              Open your authenticator app (Google Authenticator, Authy, 1Password, etc.) and scan this QR code:
            </p>
            <div className="bg-slate-50 border border-slate-200 rounded-lg p-4 inline-block">
              {qrDataUrl ? (
                <img src={qrDataUrl} alt="2FA QR code" className="w-60 h-60" />
              ) : (
                <div className="w-60 h-60 grid place-items-center text-slate-400 text-sm">Generating QR…</div>
              )}
            </div>
          </div>

          <details className="bg-slate-50 border border-slate-200 rounded p-3 text-sm">
            <summary className="cursor-pointer font-medium">Can't scan? Enter the secret manually</summary>
            <div className="mt-2 flex gap-2 items-center">
              <code className="flex-1 font-mono text-xs bg-white border rounded px-2 py-1 break-all">
                {totpData.secret}
              </code>
              <button onClick={copySecret} className="bg-slate-900 text-white px-3 py-1 rounded text-xs">
                {secretCopied ? "Copied!" : "Copy"}
              </button>
            </div>
          </details>

          <div>
            <div className="text-sm font-semibold mb-1">Step 2 — enter the 6-digit code</div>
            <form onSubmit={verify} className="flex gap-2 items-start">
              <input
                inputMode="numeric" pattern="[0-9]{6}" maxLength={6}
                className="w-44 border border-slate-300 rounded px-3 py-2 font-mono text-center text-lg tracking-[0.4em]"
                placeholder="000000" value={code}
                onChange={e => setCode(e.target.value.replace(/\D/g, ""))}
                required autoFocus
              />
              <button className="bg-brand-700 hover:bg-brand-800 text-white px-4 py-2 rounded disabled:opacity-50"
                disabled={enabling || code.length !== 6}>
                {enabling ? "Verifying…" : "Verify & enable"}
              </button>
            </form>
            {error && <div className="text-sm text-rose-700 mt-2">{error}</div>}
          </div>
        </div>
      )}

      {!status?.enabled && step === "verify" && method === "EmailOtp" && (
        <div className="bg-white border border-slate-200 rounded-lg p-6 mt-6 space-y-4">
          <div>
            <div className="text-sm font-semibold mb-1">Check your email</div>
            <p className="text-sm text-slate-600">
              We sent a 6-digit code to your account email. Enter it below to enable 2FA.
            </p>
          </div>
          <form onSubmit={verify} className="flex gap-2 items-center">
            <input
              inputMode="numeric" pattern="[0-9]{6}" maxLength={6}
              className="w-44 border border-slate-300 rounded px-3 py-2 font-mono text-center text-lg tracking-[0.4em]"
              placeholder="000000" value={code}
              onChange={e => setCode(e.target.value.replace(/\D/g, ""))}
              required autoFocus
            />
            <button className="bg-brand-700 hover:bg-brand-800 text-white px-4 py-2 rounded disabled:opacity-50"
              disabled={enabling || code.length !== 6}>
              {enabling ? "Verifying…" : "Verify & enable"}
            </button>
            <button type="button" className="text-sm text-slate-600 hover:text-slate-900 px-2"
              disabled={sendingOtp}
              onClick={() => sendEmailOtp().unwrap().catch(() => {})}>
              Resend
            </button>
          </form>
          {error && <div className="text-sm text-rose-700">{error}</div>}
        </div>
      )}

      {step === "done" && (
        <div className="bg-emerald-50 border border-emerald-200 rounded-lg p-6 mt-6 flex items-start gap-3">
          <div className="text-2xl">✅</div>
          <div>
            <div className="text-emerald-900 font-semibold">Two-factor authentication is on</div>
            <p className="text-sm text-emerald-800 mt-1">
              You'll be asked for a {method === "Totp" ? "code from your authenticator app" : "code by email"} at every sign-in.
            </p>
            <div className="mt-3 flex gap-2">
              <button className="text-sm bg-white border border-emerald-300 px-3 py-1 rounded text-emerald-800"
                onClick={() => { setStep("choose"); setCode(""); }}>
                Change method
              </button>
            </div>
          </div>
        </div>
      )}

      <div className="mt-6 bg-slate-50 border border-slate-200 rounded p-4 text-xs text-slate-600">
        <div className="font-medium text-slate-700 mb-1">🔒 Why this matters</div>
        <p>
          Even if someone discovers your password, they can't sign in without this second factor.
          Use an authenticator app for the strongest protection — phishers can intercept email codes,
          but not codes generated on your device.
        </p>
      </div>
    </div>
  );
}

function Steps({ current }: { current: "choose" | "setup" | "verify" | "done" }) {
  const steps: { key: typeof current; label: string }[] = [
    { key: "choose", label: "Choose method" },
    { key: "setup", label: "Set up" },
    { key: "verify", label: "Verify" },
    { key: "done", label: "Done" },
  ];
  const active = steps.findIndex(s => s.key === current);
  return (
    <ol className="flex gap-1 text-xs">
      {steps.map((s, i) => {
        const state = i < active ? "done" : i === active ? "active" : "pending";
        return (
          <li key={s.key}
            className={`flex-1 px-3 py-1.5 rounded border ${
              state === "active" ? "bg-brand-700 text-white border-brand-700"
                : state === "done" ? "bg-emerald-100 text-emerald-800 border-emerald-200"
                : "bg-white text-slate-500 border-slate-200"
            }`}>
            <span className="font-mono mr-1">{i + 1}.</span>{s.label}
          </li>
        );
      })}
    </ol>
  );
}

function MethodCard({ icon, title, description, recommended, onPick, disabled }: {
  icon: string; title: string; description: string;
  recommended?: boolean; onPick: () => void; disabled?: boolean;
}) {
  return (
    <button
      onClick={onPick} disabled={disabled}
      className="text-left bg-white border-2 border-slate-200 hover:border-brand-500 rounded-lg p-5 transition disabled:opacity-50">
      <div className="flex items-center justify-between mb-2">
        <div className="text-3xl">{icon}</div>
        {recommended && (
          <span className="text-xs font-medium bg-emerald-100 text-emerald-800 px-2 py-0.5 rounded-full">
            Recommended
          </span>
        )}
      </div>
      <div className="font-semibold text-slate-900">{title}</div>
      <div className="text-sm text-slate-600 mt-1">{description}</div>
    </button>
  );
}
