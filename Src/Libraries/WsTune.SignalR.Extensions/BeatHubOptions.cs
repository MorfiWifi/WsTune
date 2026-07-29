using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR.Client;

namespace WsTune.SignalR.Extensions;

public class BeatHubOptions
{
    public string Url { set; get; } = "";
    public string HeartBitFunctionName { set; get; } = "Ping";
    public int Delay {set; get;}
    public IHubInbounds? Inbound { set; get; }
    public IHubOutbounds? Outbounds { set; get; }
    // public JsonSerializerContext? JsonSerializerContext { set; get; } = null;

    /// <summary>
    /// How long the client waits for ANY message from the server before it declares the
    /// connection dead and reconnects.
    ///
    /// This timer is WALL-CLOCK based, so a forward clock jump (observed on Docker
    /// Desktop/WSL2 the container clock can jump +200s at once, and NTP resyncs jump it in
    /// production) is misread as "no message received for that long" and trips a FALSE
    /// disconnect — which then silently drops in-flight tunnel packets and desyncs the TCP
    /// stream. That was THE cause of the periodic tunnel disconnects. SignalR's 30s default
    /// (only 2x the 15s host keep-alive) makes it trivially easy to trip.
    ///
    /// We set it very high so realistic clock jumps / jitter cannot trip it. This does NOT
    /// weaken failure detection: a genuine transport drop is surfaced immediately by the
    /// WebSocket layer (not this timer) and recovered by <see cref="FastInfiniteRetryPolicy"/>,
    /// and application-level liveness is covered by the BeatHub heartbeat ("Ping").
    /// </summary>
    public TimeSpan ServerTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How often the client pings the server. Keep &lt;= half the host ClientTimeoutInterval.
    /// </summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(15);


    public Func<IHubConnectionBuilder, IHubConnectionBuilder>? CustomConfigurationsFunc;
}
