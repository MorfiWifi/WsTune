using Microsoft.AspNetCore.SignalR;
using WsTuneCli.Host;
using WsTuneCommon;
using WsTuneCommon.Models;

public class THub : Hub
{
    public async Task Forward(DataPacket packet)
    {
        var found = HostSingletons.ConnectionDetails.TryGetValue(packet.ConnectionId, out var config);
        if (found is false || config.detail is null)
            return;

        config.lastUpdate = DateTime.Now;
        await Clients.Group(config.detail.Destination).SendAsync("OnDataRceaved", packet);
    }

    public async Task Backward(DataPacket packet)
    {
        var found = HostSingletons.ConnectionDetails.TryGetValue(packet.ConnectionId, out var config);
        if (found is false || config.detail is null)
            return;

        config.lastUpdate = DateTime.Now;
        await Clients.Group(config.detail.Origin).SendAsync("OnDataRceaved", packet);
    }

    public async Task ForwardConnection(ConnectionPacket packet)
    {
        HostSingletons.ConnectionDetails[packet.ConnectionId] = (DateTime.Now, packet);

        //just for server
        await Clients.Group(packet.Destination).SendAsync("OnOpenConnection", packet);
    }

    public async Task ForwardDisConnection(DataPacket packet)
    {
        var found = HostSingletons.ConnectionDetails.TryGetValue(packet.ConnectionId, out var config);
        if (found is false || config.detail is null)
            return;

        //just for server
        await Clients.Group(config.detail.Destination).SendAsync("OnCloseConnection", packet);
        HostSingletons.ConnectionDetails.TryRemove(packet.ConnectionId, out _);
    }


    /// <summary>
    /// ping server / response back to client pong
    /// </summary>
    public void Ping()
    {
        Clients.Client(Context.ConnectionId).SendAsync(Constants.HEALTH_CHECK_RESPONSE);
    }


    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();
        if (httpContext != null)
        {
            // Access the query string parameter
            var identity = httpContext.Request.Query["identity"].ToString();

            if (string.IsNullOrEmpty(identity))
            {
                return;
            }

            await base.OnConnectedAsync();

            await Groups.AddToGroupAsync(Context.ConnectionId, identity);
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var oldKeys = HostSingletons.ConnectionDetails
            .Where(x => x.Value.lastUpdate < DateTime.Now - TimeSpan.FromMinutes(1))
            .Select(x => x.Key)
            .ToList();

        if (oldKeys.Any())
        {
            foreach (var key in oldKeys)
                HostSingletons.ConnectionDetails.TryRemove(key, out _);
        }

        await base.OnDisconnectedAsync(exception);
    }
}