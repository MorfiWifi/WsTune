namespace WsTuneCommon.Models;

public class TunnelConfigDto
{
    public string Protocol { get; set; } = "TCP";
    public int ListenPort { get; set; }
    public string TargetHost { get; set; } = "localhost";
    public int TargetPort { get; set; }
    public string Destination { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
