using WsTune.SignalR.Extensions;
using WsTuneCommon.Implementation;
using WsTuneCommon.Models;

namespace WsTuneCli.Listener.Extensions;

public static class TunnelConfigExtensions
{
    public static TcpFw4Config CreateTcpConfiguration(this TunnelConfigDto config, string identity, BasicHubOutbound hubOutbounds)
    {
        var tcpConfiguration = new TcpFw4Config
        {
            Name = config.Name,
            ListenPort = config.ListenPort,
            EnableWatchdog = true,
            OnListenerDataReceived = CreateOnListenerDataReceivedHandler(hubOutbounds),
            OnClientConnected = CreateOnClientConnectedHandler(config, identity, hubOutbounds),
            OnClientDisconnected = CreateOnClientDisconnectedHandler(hubOutbounds)
        };

        return tcpConfiguration;
    }


    public static Func<ForwardModelV4, CancellationToken, Task> CreateOnListenerDataReceivedHandler(
        BasicHubOutbound hubOutbounds)
        => async (context, token) =>
        {
            var packet = new DataPacket
            {
                ConnectionId = context.ConnectionId,
                Data = context.Data[..context.Length]
            };

            await hubOutbounds.SendAsync("Forward", packet);
        };


    public static Func<ForwardModelV4, CancellationToken, Task> CreateOnClientConnectedHandler(TunnelConfigDto config,
        string identity, BasicHubOutbound hubOutbounds)
        => async (context, token) =>
        {
            var packet = new ConnectionPacket
            {
                Origin = identity,
                Destination = config.Destination,
                ConnectionId = context.ConnectionId,
                Protocol = config.Protocol,
                Port = config.TargetPort,
                TargetHost = config.TargetHost,
            };

            ListenerSingletons.ConnectionFwName[context.ConnectionId] = context.Accessor.Name;

            await hubOutbounds.SendAsync("ForwardConnection", packet);
        };

    public static Func<ForwardModelV4, CancellationToken, Task> CreateOnClientDisconnectedHandler(
        BasicHubOutbound hubOutbounds)
        => async (context, token) =>
        {
            ListenerSingletons.ConnectionFwName.TryRemove(context.ConnectionId, out _);

            var packet = new DataPacket
            {
                ConnectionId = context.ConnectionId
            };

            await hubOutbounds.SendAsync("ForwardDisConnection", packet);
        };
}