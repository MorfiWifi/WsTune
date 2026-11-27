namespace WsTuneCommon.Models;

public class AppSettings
{
    
    /// <summary>
    /// Current Listener/server Identity as destination Address 
    /// </summary>
    public string Identity { get; set; } = string.Empty;
    
    public string SignalREndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Optional extra rendezvous endpoints (full URLs). When the primary
    /// <see cref="SignalREndpoint"/> cannot be reached, the client/server rotates through these
    /// so a single dead rendezvous does not take the tunnel down. Empty = single endpoint
    /// (original behaviour).
    /// </summary>
    public List<string> SignalRFallbackEndpoints { get; set; } = [];

    public string WebSockifyEndpoint { get; set; } = string.Empty;
    
    //listener configs
    public List<TunnelConfigDto> Configs { get; set; } = [];
    
    //server configurations
    public List<TunnelConfigDto> BlackList { get; set; } = [];
    public List<TunnelConfigDto> WhiteList { get; set; } = [];

    /// <summary>
    /// Hard cap on bytes queued in the data-plane pump before the connection is reset (so the
    /// local app reconnects) instead of buffering forever. 0 = unlimited. Default 16 MB.
    /// </summary>
    public long MaxQueuedBytes { get; set; } = 16 * 1024 * 1024;

    /// <summary>
    /// If a connection has no read/write activity for this long AND still has undrained outbound
    /// data (i.e. it is stuck), it is reset. Healthy idle sessions (no queued data) are kept.
    /// </summary>
    public TimeSpan StuckTimeout { get; set; } = TimeSpan.FromMinutes(2);

    // TCP keepalive tuning — dead network peers are detected in ~30s instead of minutes.
    public int TcpKeepAliveTimeSeconds { get; set; } = 10;
    public int TcpKeepAliveIntervalSeconds { get; set; } = 5;
    public int TcpKeepAliveRetryCount { get; set; } = 3;
}