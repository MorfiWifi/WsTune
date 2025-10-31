using Microsoft.AspNetCore.SignalR.Client;

namespace WsTune.SignalR.Extensions;

public interface IHubOutbounds
{
    void Initialize(HubConnection hubConnection);


    Task SendAsync(string method);
    Task SendAsync(string method, object arg1);
    Task SendAsync(string method, object arg1, object arg2);
}