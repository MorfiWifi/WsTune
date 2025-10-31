using WsTuneCommon.Models;

namespace WsTuneCommon.Implementation;

public class TcpFw4Config
{
    public Func<ForwardModelV4, CancellationToken, Task>? OnListenerDataReceived { set; get; }
    public Func<ForwardModelV4, CancellationToken, Task>? OnServerDataReceived { set; get; }
    public Func<ForwardModelV4, CancellationToken, Task>? OnClientConnected { set; get; }
    public Func<ForwardModelV4, CancellationToken, Task>? OnClientDisconnected { set; get; }
    public int ListenPort { set; get; } = 40000;
    public string TargetHost { set; get; } = "127.0.0.1";
    public int TargetPort { set; get; } = 5900;
    public bool EnableWatchdog { set; get; } = true;
    
    public string Name {get; set;}
}