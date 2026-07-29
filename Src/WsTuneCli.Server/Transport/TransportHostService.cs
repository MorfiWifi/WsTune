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

        var hubOutbounds = new PipelinedHubOutbound();

        var loggerFactory = LoggerFactory.Create(op => op
            .ClearProviders()
            .AddConsole()
            .SetMinimumLevel(LogLevel.Debug)
        );

        var hubLogger = loggerFactory.CreateLogger<BeatHub>();
        
        var hubInbound = new SeverHubInbounds(appSettings, hubOutbounds, cancellationToken);
        var options = GenerateHubOptions(
            hubInbound,
            hubOutbounds,
            $"{appSettings.SignalREndpoint}?identity={appSettings.Identity}&peerType=server");
        BeatHub bHub = new BeatHub(options, hubLogger);

        hubOutbounds.StartPump(cancellationToken);
        var hubTask = bHub.Start(cancellationToken);

        await Task.Delay(3_000, cancellationToken);
        
        // No upfront fw.RunAsync; handled dynamically in SeverHubInbounds
        var infTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); // Keep running


        await Task.WhenAny(hubTask, infTask);
    }

    public static BeatHubOptions GenerateHubOptions(IHubInbounds udpHubInbounds, IHubOutbounds udpHubOutbounds,
        string singlarEndpoint)
    {
        return new BeatHubOptions()
        {
            Inbound = udpHubInbounds,
            Outbounds = udpHubOutbounds,
            Delay = 60_000,
            Url = singlarEndpoint,
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
