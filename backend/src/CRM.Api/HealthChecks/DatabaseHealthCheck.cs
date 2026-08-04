using CRM.Domain.Common;
using CRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CRM.Api.HealthChecks;

/// <summary>
/// Readiness probe: verifies the application can actually reach its database. Load balancers /
/// uptime monitors hit /health/ready and should stop routing traffic to an instance whose DB is
/// unreachable, rather than serving 500s. Kept off the /health (liveness) endpoint so a transient
/// DB blip never triggers a container restart loop.
/// </summary>
public class DatabaseHealthCheck : IHealthCheck
{
    private readonly AppDbContext _db;

    public DatabaseHealthCheck(AppDbContext db) => _db = Guard.AgainstNull(db);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            return await _db.Database.CanConnectAsync(ct)
                ? HealthCheckResult.Healthy("Database reachable.")
                : HealthCheckResult.Unhealthy("Database is not reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database connectivity check threw.", ex);
        }
    }
}
