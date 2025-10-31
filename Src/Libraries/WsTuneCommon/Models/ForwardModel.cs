using WsTuneCommon.Interfaces;

namespace WsTuneCommon.Models;

public class ForwardModel
{
    public IFwV3 Accessor { get; set; }
    public byte[] Data { get; set; }
    public int Length { get; set; }
}