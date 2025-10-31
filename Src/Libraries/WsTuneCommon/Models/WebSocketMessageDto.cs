using System.Net.WebSockets;

namespace WsTuneCommon.Models;

public class WebSocketMessageDto
{
    public string ConnectionId { get; set; } = string.Empty;
    public byte[] Data { get; set; } = [];
    public WebSocketMessageType MessageType { get; set; }
    public bool IsEndOfMessage { get; set; }
}