using Microsoft.AspNetCore.SignalR.Client;

namespace WsTune.SignalR.Extensions;

public interface IHubOutbounds
{
    void Initialize(HubConnection hubConnection);


    Task SendAsync(string method);
    Task SendAsync(string method, object arg1);
    Task SendAsync(string method, object arg1, object arg2);

    /// <summary>
    /// Raised after the hub connects or reconnects. The listener uses this to replay
    /// <c>ForwardConnection</c> for still-active local TCP connections so a server restart (or
    /// a long outage) does not leave the server without routing state.
    /// </summary>
    event EventHandler? Reconnected;

    /// <summary>Raises <see cref="Reconnected"/>; called by <see cref="BeatHub"/>.</summary>
    void NotifyReconnected();
}