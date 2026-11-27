using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WsTune.SignalR.Extensions;
using WsTuneCommon;
using WsTuneCommon.Models;

namespace WsTuneCli.Server.Transport;

public class TransportHostService(
    AppSettings appSettings,
    ILogger<TransportHostService> logger
) : IHostedService
{
    private readonly List<BeatHub> _beatHubs = [];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Server identity is the address clients target: prefer the configured value, auto-generate as fallback.
        appSettings.Identity = IdentityGenerator.ResolveServer(appSettings.Identity);
        logger.LogInformation("Server identity: {Identity}", appSettings.Identity);

        var hubOutbounds = new PipelinedHubOutbound
        {
            MaxQueuedBytes = appSettings.MaxQueuedBytes
        };

        var loggerFactory = LoggerFactory.Create(op => op
            .ClearProviders()
            .AddConsole()
            .SetMinimumLevel(LogLevel.Debug)
        );

        var hubLogger = loggerFactory.CreateLogger<BeatHub>();
        
        var hubInbound = new SeverHubInbounds(appSettings, hubOutbounds, cancellationToken);
        var endpoints = BuildEndpoints(appSettings);
        var options = GenerateHubOptions(
            hubInbound,
            hubOutbounds,
            endpoints[0],
            endpoints.Skip(1).ToList());
        BeatHub bHub = new BeatHub(options, hubLogger);

        hubOutbounds.StartPump(cancellationToken);
        var hubTask = bHub.Start(cancellationToken);

        await Task.Delay(3_000, cancellationToken);
        
        // No upfront fw.RunAsync; handled dynamically in SeverHubInbounds
        var infTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); // Keep running


        await Task.WhenAny(hubTask, infTask);
    }

    /// <summary>
    /// Builds the full rendezvous URLs (primary + fallbacks) with the identity/peerType query
    /// appended, preserving any query the caller already put on the endpoint.
    /// </summary>
    public static List<string> BuildEndpoints(AppSettings appSettings)
    {
        var endpoints = new List<string>();

        void Add(string? endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
                return;

            var separator = endpoint.Contains('?') ? '&' : '?';
            endpoints.Add($"{endpoint}{separator}identity={appSettings.Identity}&peerType=server");
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
    
    


    public async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Stopping all SignalR connections...");

        foreach (var connection in _beatHubs)
        {
            await connection.StopAsync();
            await connection.DisposeAsync();
        }

        _beatHubs.Clear();
    }
}
