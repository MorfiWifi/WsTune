using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WsTune.SignalR.Extensions;
using WsTuneCommon;
using WsTuneCli.Listener.Extensions;
using WsTuneCommon.Implementation;
using WsTuneCommon.Interfaces;
using WsTuneCommon.Models;

namespace WsTuneCli.Listener.Transport;

public class TransportHostService : BackgroundService
{
    #region Ctor

    private readonly AppSettings _appSettings;
    private readonly ILogger<TransportHostService> _logger;

    public TransportHostService(AppSettings appSettings,
        ILogger<TransportHostService> logger)
    {
        _appSettings = appSettings;
        _logger = logger;
    }

    #endregion

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        // Clients always get a unique identity so multiple copies of a shared app don't collide.
        _appSettings.Identity = IdentityGenerator.ResolveClient(_appSettings.Identity);
        _logger.LogInformation("Listener identity: {Identity}", _appSettings.Identity);

        var tunnels = _appSettings.Configs;

        var fws = new Dictionary<string, IFwV4>();

        var hubOutbounds = new PipelinedHubOutbound
        {
            MaxQueuedBytes = _appSettings.MaxQueuedBytes
        };

        // Overflow/stuck reset: tear down just the offending local TCP connection so the app
        // reconnects instead of hanging on a dead flow.
        hubOutbounds.ConnectionReset = id =>
        {
            if (ListenerSingletons.ConnectionFwName.TryGetValue(id, out var name) &&
                fws.TryGetValue(name, out var fw))
            {
                return fw.ResetConnectionAsync(id);
            }

            return Task.CompletedTask;
        };
        hubOutbounds.IsDataPending = id => hubOutbounds.HasPendingData(id);

        // After a (re)connect, replay ForwardConnection for still-active local sockets so a
        // server restart / long outage does not leave the server without routing state.
        hubOutbounds.Reconnected += (_, _) =>
            _ = ListenerHubInbounds.ReplayActiveConnectionsAsync(hubOutbounds);

        var hubInbound = new ListenerHubInbounds(fws, cancellationToken);
        var endpoints = BuildEndpoints(_appSettings, "client");
        var options = GenerateHubOptions(
            hubInbound,
            hubOutbounds,
            endpoints[0],
            endpoints.Skip(1).ToList());
        BeatHub bHub = new BeatHub(options, _logger);

        hubOutbounds.StartPump(cancellationToken);
        var hubTask = bHub.Start(cancellationToken);

        //make sure connection is made (AND server is Receiving THIS)
        await Task.Delay(3_000, cancellationToken);

        var fwTasks = new List<Task>();
        foreach (var config in tunnels)
        {
            var listenerConfig = config.CreateTcpConfiguration(_appSettings.Identity , hubOutbounds);
            listenerConfig.StuckTimeout = _appSettings.StuckTimeout;
            listenerConfig.IsDataPending = id => hubOutbounds.HasPendingData(id);
            listenerConfig.TcpKeepAliveTimeSeconds = _appSettings.TcpKeepAliveTimeSeconds;
            listenerConfig.TcpKeepAliveIntervalSeconds = _appSettings.TcpKeepAliveIntervalSeconds;
            listenerConfig.TcpKeepAliveRetryCount = _appSettings.TcpKeepAliveRetryCount;

            IFwV4 fw = new TcpFwV4(listenerConfig);
            
            fws[config.Name] = fw;
            var fwTask = fw.RunAsync(CancellationToken.None);
            fwTasks.Add(fwTask);
        }

        await Task.WhenAll(fwTasks);
    }

    /// <summary>
    /// Builds the full rendezvous URLs (primary + fallbacks) with the identity/peerType query
    /// appended, preserving any query the caller already put on the endpoint.
    /// </summary>
    public static List<string> BuildEndpoints(AppSettings appSettings, string peerType)
    {
        var endpoints = new List<string>();

        void Add(string? endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
                return;

            var separator = endpoint.Contains('?') ? '&' : '?';
            endpoints.Add($"{endpoint}{separator}identity={appSettings.Identity}&peerType={peerType}");
        }

        Add(appSettings.SignalREndpoint);
        foreach (var fallback in appSettings.SignalRFallbackEndpoints)
            Add(fallback);

        if (endpoints.Count == 0)
            endpoints.Add(string.Empty);

        return endpoints;
    }
    
   public static BeatHubOptions GenerateHubOptions(IHubInbounds udpHubInbounds, IHubOutbounds udpHubOutbounds,
        string singlarEndpoint, List<string>? fallbackEndpoints = null)
    {
        return new BeatHubOptions()
        {
            Inbound = udpHubInbounds,
            Outbounds = udpHubOutbounds,
            Delay = 60_000,
            Url = singlarEndpoint,
            FallbackUrls = fallbackEndpoints ?? [],
            HeartBitFunctionName = "Ping",
            CustomConfigurationsFunc = connectionBuilder =>
                connectionBuilder.AddJsonProtocol(options =>
                    options.PayloadSerializerOptions.TypeInfoResolverChain.Insert(
                        0,
                        WsAllInOneJsonContext.Default))
        };
    }
}
