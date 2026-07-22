using CRM.Application.Common.Authorization;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using IPermissionService = CRM.Application.Common.Authorization.IPermissionService;

namespace CRM.Api.Authorization;

public class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }
    public PermissionRequirement(string permission) => Permission = Guard.AgainstNullOrWhiteSpace(permission);
}

public class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IPermissionService _permissions;
    private readonly ICurrentUser _user;

    public PermissionHandler(IPermissionService permissions, ICurrentUser user)
    {
        _permissions = Guard.AgainstNull(permissions);
        _user = Guard.AgainstNull(user);
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (_user.UserId is null) return;
        // Only SuperAdmin (global, cross-tenant) bypasses the permission framework.
        // Per-agency roles (Admin, CEO, …) must still hold the explicit permission grant.
        if (_user.Roles.Contains(CRM.Domain.Enums.Roles.SuperAdmin))
        {
            context.Succeed(requirement);
            return;
        }
        if (await _permissions.HasAsync(_user.UserId.Value, requirement.Permission))
            context.Succeed(requirement);
    }
}

public class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    private const string Prefix = "perm:";
    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public PermissionPolicyProvider(Microsoft.Extensions.Options.IOptions<AuthorizationOptions> options)
    {
        _fallback = new DefaultAuthorizationPolicyProvider(Guard.AgainstNull(options));
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(policyName.Substring(Prefix.Length)))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }
        return _fallback.GetPolicyAsync(policyName);
    }
}

public class HasPermissionAttribute : AuthorizeAttribute
{
    public HasPermissionAttribute(string permission) => Policy = $"perm:{permission}";
}
