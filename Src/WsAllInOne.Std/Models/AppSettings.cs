// namespace WsTuneCommon.Models;

using System.Collections.Generic;
// using WsTuneCommon.Models;

public class AppSettings
{
    public string SignalREndpoint { get; set; }
    public List<TunnelConfig> Configs { get; set; }
}
