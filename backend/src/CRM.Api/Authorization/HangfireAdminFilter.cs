using CRM.Domain.Common;
using CRM.Domain.Enums;
using Hangfire.Dashboard;

namespace CRM.Api.Authorization;

public class HangfireAdminFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        Guard.AgainstNull(context);
        var http = context.GetHttpContext();
        return http.User?.Identity?.IsAuthenticated == true && http.User.IsInRole(Roles.Admin);
    }
}
