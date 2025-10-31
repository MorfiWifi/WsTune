using MessagePack;

namespace WsTuneCommon.Models;



[MessagePackObject]
public class DataPacket
{
    [Key(0)] public byte[] Data { get; set; } = [];
    [Key(1)] public Guid ConnectionId { get; set; } // third-Tcp-Client
}


[MessagePackObject]
public class ConnectionPacket
{
    [Key(0)] public string Destination { get; set; } = string.Empty; //A17
    [Key(1)] public string Origin { get; set; } = string.Empty; //N56
    [Key(2)] public string Protocol { get; set; } = "TCP"; 
    [Key(3)] public int Port { get; set; } //3389
    [Key(4)] public string TargetHost { get; set; } = "127.0.0.1";
    [Key(5)] public Guid ConnectionId { get; set; } // third-Tcp-Client
    
}

public static class ConnectionPacketExtensions
{
    /// <summary>
    /// Identity = {Protocol}:{Port}
    /// </summary>
    public static string ServerIdentity(this ConnectionPacket connection) => $"[{connection.Protocol}] {connection.TargetHost}:{connection.Port}";
}