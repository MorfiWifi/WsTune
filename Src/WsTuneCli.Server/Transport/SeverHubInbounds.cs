using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR.Client;
using WsTune.SignalR.Extensions;
using WsTuneCommon.Implementation;
using WsTuneCommon.Interfaces;
using WsTuneCommon.Models;

namespace WsTuneCli.Server.Transport;

public class SeverHubInbounds : IHubInbounds
{
    #region Ctor

    private readonly ConcurrentDictionary<string, IFwV4> _fws = new();
    private readonly ConcurrentDictionary<Guid, ConnectionPacket> _connectionDetails = new();

    // Per-connection buffer for bytes that arrive before the target socket is open. Without
    // this, the client's first writes (e.g. the RFB handshake) are dropped while the server is
    // still opening the outbound connection, which silently breaks the session.
    private readonly ConcurrentDictionary<Guid, PendingConnection> _pending = new();

    private readonly AppSettings _appSettings;
    private readonly PipelinedHubOutbound _hubOutbounds;
    private readonly CancellationToken _ct;

    public SeverHubInbounds(AppSettings appSettings, PipelinedHubOutbound hubOutbounds, CancellationToken ct)
    {
        _appSettings = appSettings;
        _hubOutbounds = hubOutbounds;
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
        hub.On<ConnectionPacket>("OnOpenConnection", async (connection) =>
        {
            var connectionId = connection.ConnectionId;

            // Register the mapping FIRST so data that arrives while the outbound socket is
            // still opening is buffered (see OnDataRceaved) instead of being dropped.
            _connectionDetails[connectionId] = connection;

            var found = _fws.TryGetValue(connection.ServerIdentity(), out var fw);
            Console.WriteLine($"is running  {connection.ServerIdentity()} ? {found}");
            if (found is false || fw is null)
            {
                var canOpenSocket = CanOpenSocket(connection.Protocol, connection.TargetHost, connection.Port);
                Console.WriteLine($"Can OpenSocket {connection.ServerIdentity()} ? {canOpenSocket}");
                if (canOpenSocket is false)
                {
                    _connectionDetails.TryRemove(connectionId, out _);
                    _pending.TryRemove(connectionId, out _);
                    return;
                }

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
                _fws[connection.ServerIdentity()] = fw;
                _ = fw.RunAsync(_ct); // fire and forget!
            }

            fw = _fws[connection.ServerIdentity()];
            await fw.OpenServerConnectionAsync(connectionId, _ct);

            // Flush anything that arrived before the socket was open, preserving order, then
            // mark the connection open so subsequent data is written straight through.
            var pending = _pending.GetOrAdd(connectionId, _ => new PendingConnection());
            await pending.Gate.WaitAsync(_ct).ConfigureAwait(false);
            try
            {
                pending.Opened = true;
                if (pending.Data.Count > 0)
                {
                    foreach (var data in pending.Data)
                    {
                        await fw.SendDataToServerAsync(connectionId, data, data.Length, _ct)
                            .ConfigureAwait(false);
                    }

                    pending.Data.Clear();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Flush error: {ex.Message}");
            }
            finally
            {
                pending.Gate.Release();
            }
        });

        hub.On<DataPacket>("OnCloseConnection", async (packet) =>
        {
            var foundConnection = _connectionDetails.TryGetValue(packet.ConnectionId, out var detail);
            if (foundConnection is false || detail is null)
                return;

            _pending.TryRemove(packet.ConnectionId, out _);
            _connectionDetails.TryRemove(packet.ConnectionId, out _);

            var found = _fws.TryGetValue(detail.ServerIdentity(), out var fw);
            if (found && fw != null)
                await fw.DisposeClientAsync(packet.ConnectionId).ConfigureAwait(false);
        });


        // hub.On<DataPacket>("OnUdpDataRceaved", async (packet) => { });

        hub.On<DataPacket>("OnDataRceaved", async (packet) =>
        {
            var pending = _pending.GetOrAdd(packet.ConnectionId, _ => new PendingConnection());
            await pending.Gate.WaitAsync(_ct).ConfigureAwait(false);
            try
            {
                var found = _connectionDetails.TryGetValue(packet.ConnectionId, out var detail);
                if (pending.Opened && found && detail is not null &&
                    _fws.TryGetValue(detail.ServerIdentity(), out var fw))
                {
                    await fw.SendDataToServerAsync(packet.ConnectionId, packet.Data, packet.Data.Length, _ct)
                        .ConfigureAwait(false);
                }
                else
                {
                    // Not open yet (or mapping not ready): hold the bytes so nothing is lost.
                    pending.Data.Add(packet.Data);
                }
            }
            finally
            {
                pending.Gate.Release();
            }
        });
    }

    private Task OnServerDataReceived(ForwardModelV4 accessor, CancellationToken cancellationToken)
    {
        var packet = new DataPacket
        {
            Data = accessor.Data,
            ConnectionId = accessor.ConnectionId,
        };

        return _hubOutbounds.EnqueueDataAsync("Backward", packet, cancellationToken).AsTask();
    }

    private sealed class PendingConnection
    {
        public readonly List<byte[]> Data = new();
        public readonly SemaphoreSlim Gate = new(1, 1);
        public bool Opened;
    }
}