// namespace WsTuneCommon.Models;

public class TunnelConfig
{
    public string Id { get; set; }
    public string Protocol { get; set; }
    public int ListenPort { get; set; }
    public string TargetHost { get; set; }
    public int TargetPort { get; set; }
}
