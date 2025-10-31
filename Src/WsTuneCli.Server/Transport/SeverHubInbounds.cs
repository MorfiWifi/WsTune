using Microsoft.AspNetCore.SignalR.Client;
using WsTune.SignalR.Extensions;
using WsTuneCommon.Implementation;
using WsTuneCommon.Interfaces;
using WsTuneCommon.Models;

namespace WsTuneCli.Server.Transport;

public class SeverHubInbounds : IHubInbounds
{
    #region Ctor

    private readonly Dictionary<string, IFwV4> _fws = new();
    private readonly Dictionary<Guid, ConnectionPacket> _connectionDetails = [];

    private readonly AppSettings _appSettings;
    private readonly CancellationToken _ct;
    private HubConnection? _hub;

    public SeverHubInbounds(AppSettings appSettings, CancellationToken ct)
    {
        _appSettings = appSettings;
        _ct = ct;
    }

    #endregion


    private bool CanOpenSocket(string protocol, string host, int port)
    {
        var isWhiteList =
            _appSettings.WhiteList
                .Any(x => x.Protocol == protocol &&
                          x.TargetPort == port &&
                          x.TargetHost == host);

        var isBlackList =
            _appSettings.BlackList
                .Any(x => x.Protocol == protocol &&
                          x.TargetPort == port &&
                          x.TargetHost == host);

        return isWhiteList || !isBlackList;
    }


    public void Register(HubConnection hub)
    {
        _hub = hub;

        hub.On<ConnectionPacket>("OnOpenConnection", async (connection) =>
        {
            var found = _fws.TryGetValue(connection.ServerIdentity(), out var fw);
            Console.WriteLine($"is running  {connection.ServerIdentity()} ? {found}");
            if (found is false || fw is null)
            {
                var canOpenSocket = CanOpenSocket(connection.Protocol, connection.TargetHost, connection.Port);
                Console.WriteLine($"Can OpenSocket {connection.ServerIdentity()} ? {canOpenSocket}");
                if (canOpenSocket is false)
                    return;

                //let's do shit
                var fwConfig = new TcpFw4Config
                {
                    Name = connection.ServerIdentity() ,
                    // OnListenerDataReceived =  async (accessor, token) => { },
                    // OnClientConnected = async (accessor, token) => { },
                    // OnClientDisconnected = async (accessor, token) => { },
                    OnServerDataReceived = OnServerDataReceived,
                    EnableWatchdog = true,
                    TargetPort = connection.Port,
                    TargetHost = connection.TargetHost
                };

                fw = new TcpFwV4(fwConfig);
                _ = fw.RunAsync(_ct); // fire and forget!
                await Task.Delay(1000, _ct); // delay to start

                _fws[connection.ServerIdentity()] = fw;
            }

            _connectionDetails[connection.ConnectionId] = connection;

            fw = _fws[connection.ServerIdentity()];
            await fw.OpenServerConnectionAsync(connection.ConnectionId, _ct);
        });

        hub.On<DataPacket>("OnCloseConnection", async (packet) =>
        {
            var foundConnection = _connectionDetails.TryGetValue(packet.ConnectionId, out var detail);
            if (foundConnection is false || detail is null)
                return;

            var found = _fws.TryGetValue(detail.ServerIdentity(), out var fw);
            if (found && fw != null)
                await fw.DisposeClientAsync(packet.ConnectionId);
        });


        // hub.On<DataPacket>("OnUdpDataRceaved", async (packet) => { });

        hub.On<DataPacket>("OnDataRceaved", async (packet) =>
        {
            var found = _connectionDetails.TryGetValue(packet.ConnectionId, out var detail);
            if (found is false || detail is null)
                return; // connection not found!

            if (_fws.TryGetValue(detail.ServerIdentity(), out var fw) is false)
                return;

            await fw.SendDataToServerAsync(packet.ConnectionId, packet.Data, packet.Data.Length, _ct);
        });
    }

    private async Task OnServerDataReceived(ForwardModelV4 accessor, CancellationToken cancellationToken)
    {
        var packet = new DataPacket
        {
            Data = accessor.Data[..accessor.Length],
            ConnectionId = accessor.ConnectionId,
        };

        if (_hub is not null)
            await _hub.SendAsync("Backward", packet, cancellationToken: cancellationToken);
    }
}