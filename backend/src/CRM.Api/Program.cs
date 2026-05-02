using CRM.Api.Hubs;
using CRM.Api.Middleware;
using CRM.Api.Services;
using CRM.Application;
using CRM.Application.Common.Interfaces;
using CRM.Infrastructure;
using CRM.Infrastructure.Persistence.Seed;
using Microsoft.OpenApi;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Serilog;
using Hangfire;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => lc.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console());

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUserService>();

// Real-time agent push
builder.Services.AddSingleton<CRM.Application.Common.RealTime.IAgentNotifier,
    CRM.Api.Hubs.AgentNotifier>();

// Permission-based authorization
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider,
    CRM.Api.Authorization.PermissionPolicyProvider>();
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
    CRM.Api.Authorization.PermissionHandler>();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? new[] { "http://localhost:5173" })
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

builder.Services.AddControllers().AddJsonOptions(opts =>
{
    opts.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});
// SignalR — optional Redis backplane lets us run multiple API replicas behind
// a load balancer. Without it, hub messages stay within a single process.
//
//   SignalR:Backplane                = "InMemory" (dev / single-instance)
//                                    = "Redis"    (prod / horizontally scaled)
//   SignalR:Redis:ConnectionString   = e.g. "localhost:6379,abortConnect=false"
//   SignalR:Redis:Channel            = optional channel-prefix override
{
    var signalrBuilder = builder.Services.AddSignalR();
    if (string.Equals(builder.Configuration["SignalR:Backplane"], "Redis", StringComparison.OrdinalIgnoreCase))
    {
        var redisConn = builder.Configuration["SignalR:Redis:ConnectionString"];
        var channel   = builder.Configuration["SignalR:Redis:Channel"] ?? "crm:signalr";
        if (string.IsNullOrWhiteSpace(redisConn))
            throw new InvalidOperationException(
                "SignalR:Backplane=Redis requires SignalR:Redis:ConnectionString to be set.");
        signalrBuilder.AddStackExchangeRedis(redisConn, opts => opts.Configuration.ChannelPrefix =
            StackExchange.Redis.RedisChannel.Literal(channel));
    }
}
builder.Services.AddScoped<CRM.Application.Common.Interfaces.IChatBroadcaster, CRM.Api.Hubs.ChatBroadcaster>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "CRM API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    c.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", null)] = new List<string>()
    });
});

builder.Services.AddHealthChecks();

// ────────────────────────────────────────────────────────────────────────────
// Rate limiting — protects login from brute force, API from runaway clients,
// and webhook endpoints from upstream provider misbehaviour. Anonymous limits
// key on IP; authenticated limits key on user-id from the JWT.
//
// Tunable via Configuration["RateLimits:*"] so ops can dial it up/down per
// environment without a rebuild.
// ────────────────────────────────────────────────────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.Headers["Retry-After"] = "30";
        await ctx.HttpContext.Response.WriteAsJsonAsync(new
        {
            title = "Too many requests",
            status = 429,
            detail = "You're being rate-limited. Slow down and try again shortly.",
        }, ct);
    };

    // ── Global: every request, IP-keyed (skipped for authenticated users with
    //   their own bucket below). Generous for normal use, harsh for brute force.
    //
    // SignalR hubs (/hubs/*) negotiate frequently and burst on reconnect — they
    //   are exempt from the global limiter so a flaky network can't lock the user
    //   out of the rest of the app. Auth/webhook policies still apply elsewhere.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var path = httpContext.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase))
            return RateLimitPartition.GetNoLimiter("hub");

        var userId = httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userId))
        {
            return RateLimitPartition.GetTokenBucketLimiter("user:" + userId, _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit         = builder.Configuration.GetValue("RateLimits:User:Burst", 600),
                TokensPerPeriod    = builder.Configuration.GetValue("RateLimits:User:Refill", 300),
                ReplenishmentPeriod = TimeSpan.FromSeconds(builder.Configuration.GetValue("RateLimits:User:RefillSeconds", 60)),
                QueueLimit         = 0,
                AutoReplenishment  = true,
            });
        }

        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon";
        return RateLimitPartition.GetTokenBucketLimiter("ip:" + ip, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit         = builder.Configuration.GetValue("RateLimits:Anon:Burst", 120),
            TokensPerPeriod    = builder.Configuration.GetValue("RateLimits:Anon:Refill", 60),
            ReplenishmentPeriod = TimeSpan.FromSeconds(builder.Configuration.GetValue("RateLimits:Anon:RefillSeconds", 60)),
            QueueLimit         = 0,
            AutoReplenishment  = true,
        });
    });

    // ── Strict bucket for credential-touching endpoints. Apply via [EnableRateLimiting("auth")]
    //   on AuthController — 5 attempts / minute per IP. Standard brute-force protection.
    options.AddPolicy("auth", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon";
        return RateLimitPartition.GetFixedWindowLimiter("auth-ip:" + ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("RateLimits:Auth:PerMinute", 5),
            Window      = TimeSpan.FromMinutes(1),
            QueueLimit  = 0,
        });
    });

    // ── Webhook bucket — protects ingestion endpoints from upstream provider
    //   bursts that could DoS us.
    options.AddPolicy("webhook", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon";
        return RateLimitPartition.GetFixedWindowLimiter("hook-ip:" + ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("RateLimits:Webhook:PerMinute", 600),
            Window      = TimeSpan.FromMinutes(1),
            QueueLimit  = 0,
        });
    });
});
builder.Services.AddHostedService<CRM.Api.BackgroundJobs.SupervisorBroadcaster>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionMiddleware>();
app.UseCors();
app.UseMiddleware<IpAllowlistMiddleware>();
app.UseAuthentication();
// Force-logout-on-deactivation gate: kicks any request from an `IsActive=false`
// user even if they still hold a valid (unexpired) JWT.
app.UseMiddleware<ActiveUserGateMiddleware>();
// Rate limiter sits AFTER authentication so authenticated buckets can key on user-id.
app.UseRateLimiter();
// Hard-cap inbound `?take=` etc. so clients can't ask for unbounded result sets.
app.UseMiddleware<PaginationCapMiddleware>();
app.UseAuthorization();
app.UseMiddleware<PasswordChangeRequiredMiddleware>();

app.MapControllers();
app.MapHub<PresenceHub>("/hubs/presence");
app.MapHub<ChatHub>("/hubs/chat");
app.MapHub<SupervisorHub>("/hubs/supervisor");
app.MapHub<AgentHub>("/hubs/agent");

if (builder.Configuration.GetValue("BackgroundJobs:Provider", "InProcess")
    .Equals("Hangfire", StringComparison.OrdinalIgnoreCase))
{
    app.UseHangfireDashboard("/jobs", new Hangfire.DashboardOptions
    {
        Authorization = new[] { new CRM.Api.Authorization.HangfireAdminFilter() }
    });
    Hangfire.RecurringJob.AddOrUpdate<CRM.Infrastructure.BackgroundJobs.CallbackReminderJob>(
        "callback-reminders", j => j.RunAsync(CancellationToken.None), Hangfire.Cron.Minutely());
    Hangfire.RecurringJob.AddOrUpdate<CRM.Infrastructure.BackgroundJobs.CadenceRunnerJob>(
        "cadence-runner", j => j.RunAsync(CancellationToken.None), Hangfire.Cron.Minutely());
}
app.MapHealthChecks("/health");

using (var scope = app.Services.CreateScope())
{
    await DbSeeder.SeedAsync(scope.ServiceProvider);
}

app.Run();

public partial class Program { }
