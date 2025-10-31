using WsTuneCommon.Interfaces;

namespace WsTuneCommon.Models;

public class ForwardModelV4
{
    public Guid ConnectionId { get; set; } =  Guid.NewGuid();
    public IFwV4 Accessor { get; set; }
    public byte[] Data { get; set; }
    public int Length { get; set; }
}