using System.Collections.Concurrent;

namespace WsTuneCli.Listener;

public static class ListenerSingletons
{

    public static ConcurrentDictionary<Guid, string> ConnectionFwName { get; } = [];

}