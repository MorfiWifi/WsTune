using System.Collections.Concurrent;
using WsTuneCommon.Models;

namespace WsTuneCli.Listener;

public static class ListenerSingletons
{

    public static ConcurrentDictionary<Guid, string> ConnectionFwName { get; } = [];

    /// <summary>
    /// Active local TCP connections and the ForwardConnection payload that opened them. Used to
    /// replay <c>ForwardConnection</c> after a SignalR reconnect (e.g. a server restart) so the
    /// server can rebuild its routing state for connections that are still alive locally.
    /// </summary>
    public static ConcurrentDictionary<Guid, ConnectionPacket> ConnectionPackets { get; } = [];

}