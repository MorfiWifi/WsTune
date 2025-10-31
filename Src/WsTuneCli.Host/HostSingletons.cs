using System.Collections.Concurrent;
using WsTuneCommon.Models;

namespace WsTuneCli.Host;

public class HostSingletons
{
    public static readonly ConcurrentDictionary<Guid, (DateTime lastUpdate, ConnectionPacket detail)> ConnectionDetails = [];
}