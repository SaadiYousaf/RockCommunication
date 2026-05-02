import { Link, useLocation } from "react-router-dom";
import { useSelector } from "react-redux";
import type { RootState } from "../../app/store";
import { Button, Icon, Card, CardBody } from "../ui";

/**
 * 403 / "no access" screen rendered when a route guard rejects the current user.
 * Shows the path they tried, their roles, and offers a way back to a safe page.
 */
export function ForbiddenPage() {
  const auth = useSelector((s: RootState) => s.auth);
  const location = useLocation();

  return (
    <div className="min-h-[calc(100vh-9rem)] grid place-items-center px-4">
      <Card elevated className="max-w-lg w-full text-center">
        <CardBody className="py-10 px-8">
          <div className="h-16 w-16 mx-auto rounded-2xl bg-rose-50 text-rose-600 grid place-items-center mb-5">
            <Icon name="shield" size={28} />
          </div>
          <div className="text-[10px] font-bold tracking-[0.3em] uppercase text-rose-600 mb-2">403 · Forbidden</div>
          <h1 className="text-2xl font-semibold text-ink-900 tracking-tight">
            You don't have access to this page
          </h1>
          <p className="text-sm text-ink-600 mt-2 max-w-sm mx-auto">
            Your account ({auth.user?.userName ?? "unknown"}) doesn't have permission to view{" "}
            <code className="bg-ink-100 text-ink-800 px-1.5 py-0.5 rounded text-[11px] font-mono">
              {location.pathname}
            </code>.
            If you think this is wrong, contact an administrator.
          </p>

          <div className="mt-5 flex flex-wrap items-center justify-center gap-2">
            {(auth.user?.roles ?? []).map((r) => (
              <span key={r} className="text-[11px] font-medium px-2 py-0.5 rounded-full bg-ink-100 text-ink-700">
                {r}
              </span>
            ))}
          </div>

          <div className="flex justify-center gap-2 mt-7">
            <Link to="/dashboard">
              <Button leftIcon={<Icon name="dashboard" size={16} />}>Back to dashboard</Button>
            </Link>
            <Button variant="outline" leftIcon={<Icon name="arrowRight" size={16} className="rotate-180" />}
              onClick={() => window.history.back()}>
              Go back
            </Button>
          </div>
        </CardBody>
      </Card>
    </div>
  );
}
