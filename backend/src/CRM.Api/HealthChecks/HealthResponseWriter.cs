using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CRM.Api.HealthChecks;

/// <summary>Structured JSON body for the readiness endpoint so monitors get per-dependency detail.</summary>
public static class HealthResponseWriter
{
    public static Task WriteAsync(HttpContext ctx, HealthReport report)
    {
        ctx.Response.ContentType = "application/json";
        var payload = JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                durationMs = Math.Round(e.Value.Duration.TotalMilliseconds, 1),
            }),
        });
        return ctx.Response.WriteAsync(payload);
    }
}
