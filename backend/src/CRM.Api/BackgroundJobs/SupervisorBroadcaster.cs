using CRM.Api.Hubs;
using CRM.Application.CallCenter;
using MediatR;
using Microsoft.AspNetCore.SignalR;

namespace CRM.Api.BackgroundJobs;

public class SupervisorBroadcaster : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<SupervisorBroadcaster> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    public SupervisorBroadcaster(IServiceProvider sp, ILogger<SupervisorBroadcaster> logger)
    {
        _sp = sp;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = _sp.CreateScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                var hub = scope.ServiceProvider.GetRequiredService<IHubContext<SupervisorHub>>();

                // No tenant filter here; in multi-tenant production, push to per-agency groups.
                // For now, mediator rejects unauthenticated calls — broadcaster runs without tenant scoping.
                // Skip if no clients connected by relying on SignalR's no-op behavior.
                await hub.Clients.All.SendAsync("Heartbeat", DateTime.UtcNow, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Supervisor broadcaster tick failed");
            }
            await Task.Delay(Interval, ct);
        }
    }
}
