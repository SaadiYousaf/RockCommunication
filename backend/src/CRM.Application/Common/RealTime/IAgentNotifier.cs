namespace CRM.Application.Common.RealTime;

/// <summary>
/// Pushes events to a specific agent's open browser tabs via real-time channel.
/// Implementations target SignalR groups keyed on userId.
/// </summary>
public interface IAgentNotifier
{
    Task PushAsync(Guid userId, string eventName, object payload, CancellationToken ct = default);
}

public static class AgentEvents
{
    public const string IncomingCall = "incoming-call";
    public const string CallRinging = "call-ringing";
    public const string CallAnswered = "call-answered";
    public const string CallEnded = "call-ended";
    public const string CallStateChanged = "call-state-changed";
    public const string ScreenPop = "screen-pop";
    public const string Toast = "toast";
    /// <summary>In-app notification (pipeline forward, etc.) — the SPA toasts it.</summary>
    public const string Notification = "notification";
}
