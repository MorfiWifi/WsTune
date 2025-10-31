namespace WsTuneCommon.Models;

public class PacketDto
{
    public PacketDto()
    {
        
    }
    
    public PacketDto(Guid connectionId)
    {
        ConnectionId = connectionId;
    }
    
    public PacketDto(Guid connectionId, byte[] data)
    {
        ConnectionId = connectionId;
        Data = data;
    }

    public Guid ConnectionId { get; set; }
    public byte[] Data { get; set; } = [];
}