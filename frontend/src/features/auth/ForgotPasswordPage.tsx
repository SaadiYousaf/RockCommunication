import { useState } from "react";
import { Link } from "react-router-dom";
import { useForgotPasswordMutation } from "../../shared/api/baseApi";
import { Button, Icon, Input } from "../../shared/ui";
import { BrandLogo } from "../../shared/components/BrandLogo";

export function ForgotPasswordPage() {
  const [email, setEmail] = useState("");
  const [submitted, setSubmitted] = useState(false);
  const [forgot, { isLoading }] = useForgotPasswordMutation();

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    try { await forgot({ email }).unwrap(); }
    finally { setSubmitted(true); }
  }

  return (
    <AuthFrame title="Forgot your password?" subtitle="We'll email you a link to set a new one.">
      {submitted ? (
        <div className="space-y-4 text-center">
          <div className="mx-auto h-12 w-12 rounded-full bg-success-100 flex items-center justify-center">
            <Icon name="check" size={24} className="text-success-700" />
          </div>
          <p className="text-sm text-ink-700">
            If an account exists for <strong>{email}</strong>, a reset link is on its way.
            The link expires in 30 minutes.
          </p>
          <Link to="/login" className="text-brand-600 hover:underline text-sm">Back to sign in</Link>
        </div>
      ) : (
        <form onSubmit={submit} className="space-y-4">
          <Input
            type="email" required label="Email"
            placeholder="you@agency.com"
            leftIcon={<Icon name="users" size={16} />}
            value={email} onChange={(e) => setEmail(e.target.value)}
          />
          <Button type="submit" loading={isLoading} fullWidth size="lg">Send reset link</Button>
          <div className="text-center text-sm">
            <Link to="/login" className="text-brand-600 hover:underline">Back to sign in</Link>
          </div>
        </form>
      )}
    </AuthFrame>
  );
}

export function AuthFrame({ title, subtitle, children }: { title: string; subtitle?: string; children: React.ReactNode }) {
  return (
    <div className="min-h-screen flex items-center justify-center bg-ink-50 p-4">
      <div className="w-full max-w-md bg-white rounded-2xl shadow-soft p-8">
        <div className="flex flex-col items-center mb-6">
          <BrandLogo variant="mark" size={44} />
          <h1 className="mt-4 text-xl font-semibold text-ink-900">{title}</h1>
          {subtitle && <p className="mt-1 text-sm text-ink-500 text-center">{subtitle}</p>}
        </div>
        {children}
      </div>
    </div>
  );
}
