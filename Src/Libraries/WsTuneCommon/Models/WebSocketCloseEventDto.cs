namespace WsTuneCommon.Models;

public class WebSocketCloseEventDto : EventArgs
{
    public string ConnectionId { get; set; } = "";
    public string Reason { get; set; } = "";
}