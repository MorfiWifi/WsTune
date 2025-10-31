using Microsoft.AspNetCore.SignalR.Client;

namespace WsTune.SignalR.Extensions;

public class BasicHubOutbound : IHubOutbounds
{
    protected HubConnection? Hub;

    public void Initialize(HubConnection hubConnection)
    {
        Hub = hubConnection;
    }

    public async Task SendAsync(string method)
    {
        if (IsHubConnected())
            await Hub!.SendAsync(method);
    }

    public async Task SendAsync(string method, object arg1)
    {
        if (IsHubConnected())
            await Hub!.SendAsync(method, arg1);
    }

    public async Task SendAsync(string method, object arg1, object arg2)
    {
        if (IsHubConnected())
            await Hub!.SendAsync(method, arg1, arg2);
    }

    public bool IsHubConnected()
        => Hub is not null && Hub.State == HubConnectionState.Connected;
}