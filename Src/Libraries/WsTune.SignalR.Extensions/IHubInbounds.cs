using Microsoft.AspNetCore.SignalR.Client;

namespace WsTune.SignalR.Extensions;

/// <summary>
/// entire inbound functions for signalR
/// implement for own client
/// </summary>
public interface IHubInbounds
{
    /// <summary>
    /// register inbound function for signalR
    /// </summary>
    /// <param name="hub"></param>
    public void Register(HubConnection hub);
}