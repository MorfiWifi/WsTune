using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WsTune.SignalR.Extensions;
using WsTuneCommon;
using WsTuneCommon.Implementation;
using WsTuneCommon.Interfaces;
using WsTuneCommon.Models;

namespace WsTune.Droid;

/// <summary>
/// Android port of WsTuneCli.Listener.Transport.TransportHostService.
/// Same wire protocol (SignalR + PipelinedHubOutbound + TcpFwV4), but
/// lifecycle-aware: StartAsync/StopAsync instead of a hosted service.
/// </summary>
public sealed class ListenerRunner
{
    private static readonly ConcurrentDictionary<Guid, string> ConnectionFwName = new();

    private readonly AppSettings _settings;
    private readonly ILogger _logger;

    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private readonly List<IFwV4> _forwarders = new();

    public bool IsRunning => _runTask is { IsCompleted: false };

    public string ResolvedIdentity { get; private set; } = string.Empty;

    public ListenerRunner(AppSettings settings, ILogger logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public Task StartAsync()
    {
        if (IsRunning)
            return Task.CompletedTask;

        _cts = new CancellationTokenSource();
        _runTask = RunAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        try
        {
            _cts?.Cancel();
            foreach (var fw in _forwarders)
            {
                try { fw.RequestStop(); } catch { /* ignore */ }
            }
            if (_runTask is not null)
                await Task.WhenAny(_runTask, Task.Delay(3000));
        }
        catch { /* best effort */ }
        finally
        {
            _forwarders.Clear();
            _cts?.Dispose();
            _cts = null;
            _runTask = null;
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RunInnerAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Listener stopped.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Listener crashed.");
        }
    }

    private async Task RunInnerAsync(CancellationToken cancellationToken)
    {
        // Same as desktop: every client copy gets a unique identity.
        _settings.Identity = IdentityGenerator.ResolveClient(_settings.Identity);
        ResolvedIdentity = _settings.Identity;
        _logger.LogInformation("Listener identity: {Identity}", _settings.Identity);

        var hubOutbounds = new PipelinedHubOutbound();
        var fws = new Dictionary<string, IFwV4>();
        var hubInbound = new DroidHubInbounds(fws, cancellationToken);
        var options = new BeatHubOptions
        {
            Inbound = hubInbound,
            Outbounds = hubOutbounds,
            Delay = 60_000,
            Url = $"{_settings.SignalREndpoint}?identity={_settings.Identity}&peerType=client",
            HeartBitFunctionName = "Ping",
            CustomConfigurationsFunc = connectionBuilder =>
                connectionBuilder.AddJsonProtocol(o =>
                    o.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, WsDroidJsonContext.Default)),
        };
        var beatHub = new BeatHub(options, _logger);

        hubOutbounds.StartPump(cancellationToken);
        _ = beatHub.Start(cancellationToken);

        // Give SignalR a moment to connect (mirrors desktop 3s delay).
        await Task.Delay(3000, cancellationToken);

        var fwTasks = new List<Task>();
        foreach (var config in _settings.Configs)
        {
            var tcpConfig = CreateTcpConfiguration(config, _settings.Identity, hubOutbounds);
            IFwV4 fw = new TcpFwV4(tcpConfig);
            fws[config.Name] = fw;
            _forwarders.Add(fw);
            fwTasks.Add(fw.RunAsync(CancellationToken.None));
            _logger.LogInformation("Tunnel '{Name}' listening on port {Port} -> {Target}:{TargetPort}",
                config.Name, config.ListenPort, config.TargetHost, config.TargetPort);
        }

        await Task.WhenAll(fwTasks);
    }

    // ---- port of TunnelConfigExtensions (desktop) ----

    private static TcpFw4Config CreateTcpConfiguration(
        TunnelConfigDto config, string identity, PipelinedHubOutbound hubOutbounds)
        => new()
        {
            Name = config.Name,
            ListenPort = config.ListenPort,
            TargetHost = config.TargetHost,
            TargetPort = config.TargetPort,
            EnableWatchdog = true,
            OnListenerDataReceived = (context, token) =>
            {
                var packet = new DataPacket
                {
                    ConnectionId = context.ConnectionId,
                    Data = context.Data,
                };
                return hubOutbounds.EnqueueDataAsync("Forward", packet, token).AsTask();
            },
            OnClientConnected = async (context, token) =>
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
                ConnectionFwName[context.ConnectionId] = context.Accessor.Name;
                await hubOutbounds.SendAsync("ForwardConnection", packet);
            },
            OnClientDisconnected = async (context, token) =>
            {
                ConnectionFwName.TryRemove(context.ConnectionId, out _);
                await hubOutbounds.SendAsync("ForwardDisConnection", new DataPacket
                {
                    ConnectionId = context.ConnectionId,
                });
            },
        };

    private sealed class DroidHubInbounds : IHubInbounds
    {
        private readonly Dictionary<string, IFwV4> _fws;
        private readonly CancellationToken _cancellationToken;

        public DroidHubInbounds(Dictionary<string, IFwV4> fws, CancellationToken cancellationToken)
        {
            _fws = fws;
            _cancellationToken = cancellationToken;
        }

        public void Register(HubConnection hub)
        {
            hub.On<DataPacket>("OnDataRceaved", async packet =>
            {
                // NOTE: hub method name keeps the desktop typo ("Rceaved") for wire compat.
                if (!ConnectionFwName.TryGetValue(packet.ConnectionId, out var fwName) || fwName is null)
                    return;
                if (_fws.TryGetValue(fwName, out var fw))
                    await fw.SendDataToListenerAsync(packet.ConnectionId, packet.Data, packet.Data.Length, _cancellationToken);
            });
        }
    }
}
