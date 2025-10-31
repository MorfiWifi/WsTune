using MessagePack;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using WsTuneCli.Listener.Extensions;
using WsTuneCommon.Implementation;
using WsTuneCommon.Interfaces;
using WsTuneCommon.Models;

namespace WsTuneCli.Listener.Transport;

public class TransportHostService /*: IHostedService*/
{
    private readonly AppSettings _appSettings;

    public TransportHostService(AppSettings appSettings)
    {
        _appSettings = appSettings;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var tunnels = _appSettings.Configs;

        var fws = new Dictionary<string, IFwV4>();

        var hubInbound = new ListenerHubInbounds(fws, cancellationToken);
        
        var connection = new HubConnectionBuilder()
            .WithUrl( $"{_appSettings.SignalREndpoint}?identity={_appSettings.Identity}")
            .WithAutomaticReconnect()
            .AddMessagePackProtocol(op => 
                op.SerializerOptions = MessagePackSerializerOptions.Standard
                .WithCompression(MessagePackCompression.Lz4BlockArray)
            )
            .Build();

        hubInbound.Register(connection);
        
        // await connection.StartAsync(cancellationToken);
        var task =  connection.StartAsync(cancellationToken);
        

        //make sure connection is made (AND server is Receiving THIS)
        await Task.Delay(3_000, cancellationToken);
        
        var fwTasks = new List<Task>();
        foreach (var config in tunnels)
        {
            var listenerConfig = config.CreateTcpConfiguration(_appSettings.Identity , connection);
            
            IFwV4 fw = new TcpFwV4(listenerConfig);
            fws[config.Name] = fw;
            var fwTask = fw.RunAsync(CancellationToken.None);
            fwTasks.Add(fwTask);
        }

        await Task.WhenAll(fwTasks);
    }
    
}