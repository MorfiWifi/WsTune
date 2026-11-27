using Microsoft.AspNetCore.SignalR.Client;
using WsTune.SignalR.Extensions;
using WsTuneCommon.Interfaces;
using WsTuneCommon.Models;

namespace WsTuneCli.Listener.Transport;

public class ListenerHubInbounds : IHubInbounds
{
    #region Ctor

    private readonly CancellationToken _cancellationToken;
    private readonly Dictionary<string, IFwV4> _fws;

    public ListenerHubInbounds(Dictionary<string, IFwV4> fws, CancellationToken cancellationToken)
    {
        _fws = fws;
        _cancellationToken = cancellationToken;
    }

    #endregion

    public void Register(HubConnection hub)
    {
        hub.On<DataPacket>("OnDataRceaved", async (packet) =>
        {
            var found = ListenerSingletons.ConnectionFwName.TryGetValue(packet.ConnectionId, out var fwName);
            if (found is false || fwName is null)
                return;

            if (_fws.TryGetValue(fwName, out var fw))
            {
                await fw.SendDataToListenerAsync(packet.ConnectionId, packet.Data, packet.Data.Length, _cancellationToken);
            }
        });
    }

    /// <summary>
    /// Re-sends <c>ForwardConnection</c> for every still-active local TCP connection. Called after
    /// the hub (re)connects so a server restart or long outage does not leave the server without
    /// routing state for connections that are alive on the client. The server opens idempotently.
    /// </summary>
    public static async Task ReplayActiveConnectionsAsync(PipelinedHubOutbound hubOutbounds)
    {
        foreach (var kv in ListenerSingletons.ConnectionPackets.ToArray())
        {
            try
            {
                await hubOutbounds.SendAsync("ForwardConnection", kv.Value).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Replay ForwardConnection error: {ex.Message}");
            }
        }
    }
}