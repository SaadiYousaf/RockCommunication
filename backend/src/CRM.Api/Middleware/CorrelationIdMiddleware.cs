using CRM.Domain.Common;
using Serilog.Context;

namespace CRM.Api.Middleware;

/// <summary>
/// Assigns every request a stable correlation id (honouring an inbound X-Correlation-ID / the edge's
/// X-Request-Id when present, else a fresh GUID), threads it through Serilog's LogContext so EVERY log
/// line emitted while handling the request carries it, and echoes it back on the response so a client,
/// nginx, or an aggregated log can tie a user report to the exact server-side trace. Runs first in the
/// pipeline so even errors are correlated.
/// </summary>
public class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = Guard.AgainstNull(next);

    public async Task Invoke(HttpContext ctx)
    {
        Guard.AgainstNull(ctx);

        var correlationId = ctx.Request.Headers.TryGetValue(HeaderName, out var inbound) && !string.IsNullOrWhiteSpace(inbound)
            ? inbound.ToString()
            : ctx.Request.Headers.TryGetValue("X-Request-Id", out var edge) && !string.IsNullOrWhiteSpace(edge)
                ? edge.ToString()
                : Guid.NewGuid().ToString("n");

        // Expose it to MVC/handlers and reflect it back before the response starts.
        ctx.Items[HeaderName] = correlationId;
        ctx.Response.OnStarting(() =>
        {
            ctx.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        // AsyncLocal scope flows through the whole async pipeline, so every downstream log line
        // (including Serilog's request-completion entry) is stamped with CorrelationId.
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(ctx);
        }
    }
}
