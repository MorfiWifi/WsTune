using System.Collections.Concurrent;
using WsTune.SignalR.Extensions;
using WsTuneCli.Listener.Transport;
using WsTuneCommon.Interfaces;
using WsTuneCommon.Models;

namespace WsAllInOne;

public class Storage
{
    //container
    public static ConcurrentDictionary<string, TunnelConfigDto> Configs =
        new(
        [
            new KeyValuePair<string, TunnelConfigDto>(
                "A17-VNC",
                new TunnelConfigDto
                {
                    ListenPort = 40000,
                    TargetHost = "127.0.0.1",
                    TargetPort = 5900,
                    Destination = "ASUS-A17",
                    Name = "A17-VNC",
                    Protocol = "TCP"
                }),

            new KeyValuePair<string, TunnelConfigDto>(
                "N56-VNC",
                new TunnelConfigDto
                {
                    ListenPort = 40001,
                    TargetHost = "127.0.0.1",
                    TargetPort = 5900,
                    Destination = "ASUS-N56",
                    Name = "N56-VNC",
                    Protocol = "TCP"
                })
        ]);


    public static BasicHubOutbound? hubOutbounds;
    public static ListenerHubInbounds? hubInbound;
    public static Dictionary<string, IFwV4> fws = [];
    
    public static string ListenerIdentity = String.Empty;
    
    
    //This is docker image port
    public static int DefaultHttpPort = 8080;
}