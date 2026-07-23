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

    public string? IpAddress => _accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}
