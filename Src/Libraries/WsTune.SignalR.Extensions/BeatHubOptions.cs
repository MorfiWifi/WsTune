using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR.Client;

namespace WsTune.SignalR.Extensions;

public class BeatHubOptions
{
    public string Url { set; get; } = "";
    public string HeartBitFunctionName { set; get; } = "Ping";
    public int Delay {set; get;}
    public IHubInbounds Inbound { set; get; }
    public IHubOutbounds Outbounds { set; get; }
    // public JsonSerializerContext? JsonSerializerContext { set; get; } = null;

    /// <summary>
    /// How long the client waits for ANY message from the server before it declares the
    /// connection dead and reconnects. SignalR's default is 30s while the host keep-alive
    /// is 15s — only a 2x margin, so a delayed keep-alive (e.g. from network jitter or a
    /// brief scheduling hiccup) trips a FALSE disconnect, which then silently drops
    /// in-flight tunnel packets and desyncs the TCP stream. 60s gives a 4x margin (the
    /// recommended hardened value). A genuine transport drop is detected immediately by the
    /// WebSocket layer, not this timer, so recovery on a real drop is unaffected.
    /// </summary>
    public TimeSpan ServerTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How often the client pings the server. Keep &lt;= half the host ClientTimeoutInterval.
    /// </summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(15);


    public Func<IHubConnectionBuilder, IHubConnectionBuilder>? CustomConfigurationsFunc;
}