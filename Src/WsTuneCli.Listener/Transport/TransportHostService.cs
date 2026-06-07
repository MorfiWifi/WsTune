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
        var hubOutbounds = new PipelinedHubOutbound();
        
        var tunnels = _appSettings.Configs;

        var fws = new Dictionary<string, IFwV4>();

        var hubInbound = new ListenerHubInbounds(fws, cancellationToken);
        var options = GenerateHubOptions(hubInbound, hubOutbounds, $"{_appSettings.SignalREndpoint}?identity={_appSettings.Identity}");
        BeatHub bHub = new BeatHub(options, _logger);

        hubOutbounds.StartPump(cancellationToken);
        var hubTask = bHub.Start(cancellationToken);

        //make sure connection is made (AND server is Receiving THIS)
        await Task.Delay(3_000, cancellationToken);

        var fwTasks = new List<Task>();
        foreach (var config in tunnels)
        {
            var listenerConfig = config.CreateTcpConfiguration(_appSettings.Identity , hubOutbounds);
            
            IFwV4 fw = new TcpFwV4(listenerConfig);
            
            fws[config.Name] = fw;
            var fwTask = fw.RunAsync(CancellationToken.None);
            fwTasks.Add(fwTask);
        }

        await Task.WhenAll(fwTasks);
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
                connectionBuilder.AddMessagePackProtocol(options =>
                    options.SerializerOptions = TunnelMessagePackOptions.SignalR)
        };
    }
}