// using WsTune.SignalR.Extensions;

using Microsoft.AspNetCore.SignalR.Client;
using WsTuneCommon.Implementation;
using WsTuneCommon.Models;

namespace WsTuneCli.Listener.Extensions;

public static class TunnelConfigExtensions
{
    public static TcpFw4Config CreateTcpConfiguration(this TunnelConfigDto config, string identity, HubConnection hubOutbounds)
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
        HubConnection hubOutbounds)
        => async (context, token) =>
        {
            var packet = new DataPacket
            {
                ConnectionId = context.ConnectionId,
                Data = new byte[context.Length],
            };
            
            Array.Copy(context.Data, 0, packet.Data, 0, context.Length);

            await hubOutbounds.SendAsync("Forward", packet);
        };


    public static Func<ForwardModelV4, CancellationToken, Task> CreateOnClientConnectedHandler(TunnelConfigDto config,
        string identity, HubConnection hubOutbounds)
        => async (context, token) =>
        {
            var packet = new ConnectionPacket
            {
                Origin = identity,
                Destination = config.Destination,
                ConnectionId = context.ConnectionId,
                Protocol = config.Protocol,
                TargetHost = config.TargetHost,
                Port = config.TargetPort,
            };

            ListenerSingletons.ConnectionFwName[context.ConnectionId] = context.Accessor.Name;

            await hubOutbounds.SendAsync("ForwardConnection", packet, cancellationToken: token);
        };

    public static Func<ForwardModelV4, CancellationToken, Task> CreateOnClientDisconnectedHandler(
        HubConnection hubOutbounds)
        => async (context, token) =>
        {
            ListenerSingletons.ConnectionFwName.TryRemove(context.ConnectionId, out _);

            var packet = new DataPacket
            {
                ConnectionId = context.ConnectionId
            };

            await hubOutbounds.SendAsync("ForwardDisConnection", packet, cancellationToken: token);
        };
}