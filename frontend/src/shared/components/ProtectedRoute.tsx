import { Navigate, Outlet, useLocation } from "react-router-dom";
import { useSelector } from "react-redux";
import type { RootState } from "../../app/store";
import { ForbiddenPage } from "./ForbiddenPage";

interface ProtectedRouteProps {
  roles?: string[];
  modules?: string[];
}

/**
 * Route guard.
 * - `roles`:   succeed if the user has any of these role names.
 * - `modules`: succeed if the user has any of these module codes.
 * If both are supplied, EITHER match grants access.
 *
 * Behaviour:
 *   - Not authenticated  -> redirect to /login
 *   - Authenticated but unauthorized -> render the ForbiddenPage in place
 *     (so the user lands on a clear "no access" screen for that exact URL
 *     instead of a silent redirect).
 *   - Admin role bypasses the check entirely.
 */
export function ProtectedRoute({ roles, modules }: ProtectedRouteProps) {
  const auth = useSelector((s: RootState) => s.auth);
  const { pathname } = useLocation();
  if (!auth.accessToken || !auth.user) return <Navigate to="/login" replace />;

  // Force first-time password change before any other route is accessible.
  if (auth.user.mustChangePassword && pathname !== "/change-password") {
    return <Navigate to="/change-password" replace />;
  }

  const userRoles = auth.user.roles ?? [];
  const userModules = auth.user.modules ?? [];

  // SuperAdmin = global admin (no agency). Admin = per-agency owner. Both bypass module
  // gating; the underlying API still enforces permission/tenant rules.
  if (userRoles.includes("SuperAdmin") || userRoles.includes("Admin")) return <Outlet />;
  if (!roles && !modules) return <Outlet />;

  const roleMatch = roles ? roles.some((r) => userRoles.includes(r)) : false;
  const moduleMatch = modules ? modules.some((m) => userModules.includes(m)) : false;

  if (!roleMatch && !moduleMatch) return <ForbiddenPage />;
  return <Outlet />;
}
