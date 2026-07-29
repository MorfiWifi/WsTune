namespace WsTuneCommon.Models;

public class DataPacket
{
    public byte[] Data { get; set; } = [];
    public Guid ConnectionId { get; set; } // third-Tcp-Client
}

public class ConnectionPacket
{
    public string Destination { get; set; } = string.Empty; //A17
    public string Origin { get; set; } = string.Empty; //N56
    public string Protocol { get; set; } = "TCP";
    public int Port { get; set; } //3389
    public string TargetHost { get; set; } = "127.0.0.1";
    public Guid ConnectionId { get; set; } // third-Tcp-Client
    
}

public static class ConnectionPacketExtensions
{
    /// <summary>
    /// Identity = {Protocol}:{Port}
    /// </summary>
    public static string ServerIdentity(this ConnectionPacket connection) => $"[{connection.Protocol}] {connection.TargetHost}:{connection.Port}";
}
