namespace WsTuneCommon.Models;

public class AppSettings
{
    
    /// <summary>
    /// Current Listener/server Identity as destination Address 
    /// </summary>
    public string Identity { get; set; } = string.Empty;
    
    public string SignalREndpoint { get; set; }
    public string WebSockifyEndpoint { get; set; }
    
    //listener configs
    public List<TunnelConfigDto> Configs { get; set; }
    
    //server configurations
    public List<TunnelConfigDto> BlackList { get; set; } = [];
    public List<TunnelConfigDto> WhiteList { get; set; } = [];
}
