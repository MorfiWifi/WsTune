using WsTune.SignalR.Extensions;
using WsTuneCommon.Implementation;
using WsTuneCommon.Models;

namespace WsTuneCli.Listener.Extensions;

public static class TunnelConfigExtensions
{
    public static TcpFw4Config CreateTcpConfiguration(this TunnelConfigDto config, string identity, PipelinedHubOutbound hubOutbounds)
    {
        var tcpConfiguration = new TcpFw4Config
        {
            Name = config.Name,
            ListenPort = config.ListenPort,
            TargetHost = config.TargetHost,
            TargetPort = config.TargetPort,
            EnableWatchdog = true,
            OnListenerDataReceived = CreateOnListenerDataReceivedHandler(hubOutbounds),
            OnClientConnected = CreateOnClientConnectedHandler(config, identity, hubOutbounds),
            OnClientDisconnected = CreateOnClientDisconnectedHandler(hubOutbounds)
        };

        return tcpConfiguration;
    }


    public static Func<ForwardModelV4, CancellationToken, Task> CreateOnListenerDataReceivedHandler(
        PipelinedHubOutbound hubOutbounds)
        => (context, token) =>
        {
            var packet = new DataPacket
            {
                ConnectionId = context.ConnectionId,
                Data = context.Data
            };

            return hubOutbounds.EnqueueDataAsync("Forward", packet, token).AsTask();
        };


    public static Func<ForwardModelV4, CancellationToken, Task> CreateOnClientConnectedHandler(TunnelConfigDto config,
        string identity, PipelinedHubOutbound hubOutbounds)
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
        PipelinedHubOutbound hubOutbounds)
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
