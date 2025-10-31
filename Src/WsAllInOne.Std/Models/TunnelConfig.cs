using MessagePack;

// namespace WsTuneCommon.Models;

[MessagePackObject]
public class TunnelConfig
{
    [Key(0)] public string Id { get; set; }
    [Key(1)] public string Protocol { get; set; }
    [Key(2)] public int ListenPort { get; set; }
    [Key(3)] public string TargetHost { get; set; }
    [Key(4)] public int TargetPort { get; set; }
}