using MessagePack;

// namespace WsTuneCommon.Models;

[MessagePackObject]
public class DataPacket
{
    [Key(0)] public byte[] Data { get; set; }
    [Key(1)] public int Length { get; set; }
    [Key(2)] public string TunnelId { get; set; }
}