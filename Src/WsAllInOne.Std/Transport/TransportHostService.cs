using Microsoft.AspNetCore.SignalR.Client;
using WsTuneCli.Listener.Extensions;
using WsTuneCommon;
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
        // Clients always get a unique identity so multiple copies of a shared app don't collide.
        _appSettings.Identity = IdentityGenerator.ResolveClient(_appSettings.Identity);
        Console.WriteLine($"Listener identity: {_appSettings.Identity}");

        var tunnels = _appSettings.Configs;

        var fws = new Dictionary<string, IFwV4>();

        var hubInbound = new ListenerHubInbounds(fws, cancellationToken);
        
        var connection = new HubConnectionBuilder()
            .WithUrl( $"{_appSettings.SignalREndpoint}?identity={_appSettings.Identity}")
            .WithAutomaticReconnect()
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
