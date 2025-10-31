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


    public Func<IHubConnectionBuilder, IHubConnectionBuilder>? CustomConfigurationsFunc;
}