using Microsoft.AspNetCore.SignalR.Client;
// using WsTune.SignalR.Extensions;
using WsTuneCommon.Interfaces;
using WsTuneCommon.Models;

namespace WsTuneCli.Listener.Transport;

public class ListenerHubInbounds /*: IHubInbounds*/
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
}