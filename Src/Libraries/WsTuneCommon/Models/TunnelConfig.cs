using MessagePack;

namespace WsTuneCommon.Models;

[MessagePackObject]
public class TunnelConfigDto
{
    [Key(0)] public string Protocol { get; set; } = "TCP";
    [Key(1)] public int ListenPort { get; set; }
    [Key(2)] public string TargetHost { get; set; } = "localhost";
    [Key(3)] public int TargetPort { get; set; }
    [Key(4)] public string Destination { get; set; } = string.Empty;
    [IgnoreMember] public string Name { get; set; } = string.Empty;
}