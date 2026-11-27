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

    /// <summary>
    /// How long a connection with no read/write activity is kept before the watchdog tears it
    /// down. Defaults to 30 minutes (was 5) so an idle remote-desktop session is not dropped;
    /// set higher to effectively disable idle reaping, or <see cref="EnableWatchdog"/> = false.
    /// </summary>
    public TimeSpan IdleTimeout { set; get; } = TimeSpan.FromMinutes(30);

    /// <summary>How often the watchdog scans for idle connections.</summary>
    public TimeSpan CheckInterval { set; get; } = TimeSpan.FromMinutes(1);

    public string Name { get; set; } = string.Empty;
}
