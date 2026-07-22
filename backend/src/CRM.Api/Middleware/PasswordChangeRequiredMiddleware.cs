using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using System.Net;
using System.Text.Json;

namespace CRM.Api.Middleware;

/// <summary>
/// When an access token carries <c>pwd_change=true</c>, every endpoint outside this
/// allowlist returns 403. This is the server-side counterpart of the FE redirect — the
/// FE can be bypassed; the API cannot. The user can only change their password or sign
/// out until the flag is cleared.
/// </summary>
public class PasswordChangeRequiredMiddleware
{
    private readonly RequestDelegate _next;

    // Endpoints a "must-change-password" user is allowed to hit.
    private static readonly string[] AllowedPaths =
    {
        "/api/auth/change-password",
        "/api/auth/logout",
        "/api/auth/me",
        "/api/auth/refresh",
        "/health",
    };

    public PasswordChangeRequiredMiddleware(RequestDelegate next) => _next = Guard.AgainstNull(next);

    public async Task Invoke(HttpContext ctx)
    {
        Guard.AgainstNull(ctx);
        var user = ctx.User;
        if (user?.Identity?.IsAuthenticated == true &&
            string.Equals(user.FindFirst(CustomJwtClaims.PasswordChangeRequired)?.Value, "true",
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
                    title = "Password change required",
                    status = (int)HttpStatusCode.Forbidden,
                    detail = "You must change your password before using the rest of the application.",
                    code = "password_change_required",
                }));
                return;
            }
        }

        await _next(ctx);
    }
}
