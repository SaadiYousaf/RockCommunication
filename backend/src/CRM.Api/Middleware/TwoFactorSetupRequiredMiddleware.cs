using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using System.Net;
using System.Text.Json;

namespace CRM.Api.Middleware;

/// <summary>
/// When an access token carries <c>twofa_setup=true</c> (a privileged user who hasn't
/// enrolled in two-factor auth), every endpoint outside this allowlist returns 403 — the
/// server-side counterpart to the FE "/setup-2fa" redirect. Directly mirrors
/// <see cref="PasswordChangeRequiredMiddleware"/>; runs after it, so if both flags are set
/// the password change is completed first, then 2FA enrolment.
/// </summary>
public class TwoFactorSetupRequiredMiddleware
{
    private readonly RequestDelegate _next;

    // Only the 2FA setup/enrolment surface (plus the shared auth allowlist) is reachable.
    private static readonly string[] AllowedPaths =
    {
        "/api/auth/2fa/setup",
        "/api/auth/2fa/enable",
        "/api/auth/2fa/status",
        "/api/auth/2fa/method",
        "/api/auth/2fa/email/send-otp",
        "/api/auth/2fa/verify",
        "/api/auth/change-password",
        // Re-authentication MUST stay reachable — the FE attaches the (setup-required) token to
        // every request incl. login, so without these a stuck user can never log back in to enrol.
        "/api/auth/login",
        "/api/auth/logout",
        "/api/auth/me",
        "/api/auth/refresh",
        "/health",
    };

    public TwoFactorSetupRequiredMiddleware(RequestDelegate next) => _next = Guard.AgainstNull(next);

    public async Task Invoke(HttpContext ctx)
    {
        Guard.AgainstNull(ctx);
        var user = ctx.User;
        if (user?.Identity?.IsAuthenticated == true &&
            string.Equals(user.FindFirst(CustomJwtClaims.TwoFactorSetupRequired)?.Value, "true",
                StringComparison.OrdinalIgnoreCase))
        {
            var path = ctx.Request.Path.Value ?? string.Empty;
            var allowed = AllowedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
            if (!allowed)
            {
                ctx.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                ctx.Response.ContentType = "application/problem+json";
                await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    title = "Two-factor setup required",
                    status = (int)HttpStatusCode.Forbidden,
                    detail = "Two-factor authentication is mandatory for your role. Set it up before using the rest of the application.",
                    code = "two_factor_setup_required",
                }));
                return;
            }
        }

        await _next(ctx);
    }
}
