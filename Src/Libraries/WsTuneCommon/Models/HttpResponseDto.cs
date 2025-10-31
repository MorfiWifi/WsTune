using System.Text;

namespace WsTuneCommon.Models;

public class HttpResponseDto
{
    public string RequestId { get; set; } = string.Empty;
    public int StatusCode { get; set; }

    public byte[] Body { get; set; } = []; // Changed to byte[]
    public string ContentType { get; set; } = string.Empty;
    
    public Dictionary<string, string?[]> Headers { get; set; } = [];
    
    
    public HttpResponseDto()
    {
    }

    public HttpResponseDto(string requestId, string message, Exception? e = null)
    {
        var bodyBytes = Encoding.UTF8.GetBytes($"{message} {e?.Message}");
        RequestId = requestId;
        StatusCode = 500;
        Body = bodyBytes; // Send byte array directly
        ContentType = "text/plain";
    }
}