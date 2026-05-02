namespace CRM.Api.Middleware;

/// <summary>
/// Hard-caps any inbound `?take=` (and aliases like `?pageSize=`, `?limit=`)
/// on every API request. Prevents clients from asking for 1,000,000 rows and
/// blowing the database / serializer up. Per-controller defaults still apply
/// for normal page sizes; this is a safety net.
///
/// Default cap = 200. Override via Configuration["Pagination:MaxTake"].
/// </summary>
public class PaginationCapMiddleware
{
    private static readonly string[] _params = { "take", "pageSize", "limit", "size" };
    private readonly RequestDelegate _next;
    private readonly int _maxTake;

    public PaginationCapMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next = next;
        _maxTake = config.GetValue("Pagination:MaxTake", 200);
    }

    public async Task Invoke(HttpContext ctx)
    {
        // Only inspect API requests; webhooks / static files don't need this.
        if (ctx.Request.Path.StartsWithSegments("/api"))
        {
            var q = ctx.Request.Query;
            var changed = false;
            var copy = new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(q.Count);
            foreach (var kv in q)
            {
                if (Array.IndexOf(_params, kv.Key) >= 0
                    && int.TryParse(kv.Value.ToString(), out var v)
                    && v > _maxTake)
                {
                    copy[kv.Key] = _maxTake.ToString();
                    changed = true;
                }
                else
                {
                    copy[kv.Key] = kv.Value;
                }
            }
            if (changed)
            {
                ctx.Request.Query = new Microsoft.AspNetCore.Http.QueryCollection(copy);
                ctx.Response.Headers["X-Pagination-Capped"] = _maxTake.ToString();
            }
        }
        await _next(ctx);
    }
}
