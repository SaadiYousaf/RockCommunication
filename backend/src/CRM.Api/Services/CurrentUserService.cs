using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using System.Security.Claims;

namespace CRM.Api.Services;

public class CurrentUserService : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUserService(IHttpContextAccessor accessor) => _accessor = Guard.AgainstNull(accessor);

    private ClaimsPrincipal? User => _accessor.HttpContext?.User;

    public Guid? UserId
    {
        get
        {
            var raw = User?.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User?.FindFirstValue(CustomJwtClaims.Subject);
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public string? UserName => User?.FindFirstValue("unique_name") ?? User?.Identity?.Name;

    public Guid? AgencyId
    {
        get
        {
            var raw = User?.FindFirstValue(CustomJwtClaims.Agency);
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public Guid? CallCenterId
    {
        get
        {
            var raw = User?.FindFirstValue(CustomJwtClaims.CallCenter);
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public IReadOnlyList<string> Roles => User?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList() ?? new List<string>();

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    /// <summary>
    /// The real client IP. Behind Cloudflare + nginx the socket address is just the proxy
    /// (127.0.0.1 / "localhost"), so prefer the forwarded headers the proxies set: Cloudflare's
    /// CF-Connecting-IP first (authoritative when traffic only enters via Cloudflare), then the
    /// left-most X-Forwarded-For entry, then X-Real-IP, and only then the raw socket address.
    /// </summary>
    public string? IpAddress
    {
        get
        {
            var ctx = _accessor.HttpContext;
            if (ctx is null) return null;

            string? FromHeader(string name)
            {
                var raw = ctx.Request.Headers[name].ToString();
                if (string.IsNullOrWhiteSpace(raw)) return null;
                var first = raw.Split(',')[0].Trim();
                return string.IsNullOrWhiteSpace(first) ? null : first;
            }

            return FromHeader("CF-Connecting-IP")
                ?? FromHeader("X-Forwarded-For")
                ?? FromHeader("X-Real-IP")
                ?? ctx.Connection.RemoteIpAddress?.ToString();
        }
    }
}
