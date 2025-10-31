using MessagePack;
using WsTune.SignalR.Extensions;
using WsTuneCli.Listener.Transport;
using WsTuneCommon.Models;

namespace WsAllInOne.Services;

public class InternalHubService : BackgroundService
{
    #region Ctor

    private readonly ILogger _logger;
    private readonly AppSettings _appSettings;

    public InternalHubService(ILogger<InternalHubService> logger, AppSettings appSettings)
    {
        _logger = logger;
        _appSettings = appSettings;
    }

    #endregion

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        //allow http server warm up
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        
        Storage.hubOutbounds = new BasicHubOutbound();
        Storage.hubInbound = new ListenerHubInbounds(Storage.fws, cancellationToken);
        
        var options = GenerateHubOptions(Storage.hubInbound, Storage.hubOutbounds, $"http://127.0.0.1:{Storage.DefaultHttpPort}{_appSettings.SignalREndpoint}?identity={Storage.ListenerIdentity}");
        BeatHub bHub = new BeatHub(options, _logger);

        await bHub.Start(cancellationToken);
    }
    
    public static BeatHubOptions GenerateHubOptions(IHubInbounds udpHubInbounds, BasicHubOutbound udpHubOutbounds,
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
            {
                return connectionBuilder.AddMessagePackProtocol(options =>
                {
                    // Optional: customize MessagePack settings
                    options.SerializerOptions = MessagePackSerializerOptions.Standard
                        .WithCompression(MessagePackCompression.Lz4BlockArray);
                });
            }
        };
    }
}
