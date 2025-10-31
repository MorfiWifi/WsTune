// using Microsoft.AspNetCore.Http;
// using WsTuneCommon.Extensions;
//
// namespace WsTuneCommon.Models;
//
// public class HttpRequestDto
// {
//     public string RequestId { get; set; } = string.Empty;
//     public string Method { get; set; } = string.Empty;
//     public string Path { get; set; } = string.Empty;
//     public Dictionary<string, string?[]>? QueryParams { get; set; }
//     public Dictionary<string, string[]> Headers { get; set; } = [];
//     public byte[] Body { get; set; } = []; // Changed to byte[]
//     public string? ContentType { get; set; }
//     public string Scheme { get; set; } = "http";
//     public string Protocol { get; set; } = "http";
//     public bool IsWebSocketUpgrade { get; set; }
//
//     public string BaseUrl { get; set; } = string.Empty;
//
//     /// <summary>
//     /// just for serialization 
//     /// </summary>
//     public HttpRequestDto()
//     {
//     }
//
//     public HttpRequestDto(string requestId, string baseUrl, HttpContext context)
//     {
//         var request = context.Request;
//
//         BaseUrl = baseUrl;
//         RequestId = requestId;
//         Method = request.Method;
//         Path = request.Path;
//         QueryParams = request.Query.ToDict();
//         ContentType = request.ContentType;
//         Scheme = request.Scheme;
//         Protocol = request.Protocol;
//         IsWebSocketUpgrade = context.WebSockets.IsWebSocketRequest;
//
//         SetHeaders(context);
//     }
//
//     public async Task SetRequestBodyAsync(HttpContext context)
//     {
//         var request = context.Request;
//
//         if (request is { ContentLength: > 0, Body.CanRead: true })
//         {
//             using var memoryStream = new MemoryStream();
//             await request.Body.CopyToAsync(memoryStream);
//             Body = memoryStream.ToArray();
//         }
//     }
//
//
//     private void SetHeaders(HttpContext context)
//     {
//         var request = context.Request;
//         var headers = request.Headers.ToDict(); // Serialize headers
//
//         FixWebSocketCompressionHeader(headers);
//
//         Headers = headers;
//         Headers["X-Real-IP"] = [context.Connection.RemoteIpAddress?.ToString() ?? ""];
//         Headers["HOST"] = [context.Request.Host.Value];
//         Headers["Origin"] = [$"{Scheme}://{context.Request.Host.Value}"];
//     }
//
//
//     /// <summary>
//     /// remove compression support for webSocket (in secure/ not supported by microsoft) 
//     /// </summary>
//     /// <param name="headers"></param>
//     private static void FixWebSocketCompressionHeader(Dictionary<string, string?[]> headers)
//     {
//         var hasWebSec = headers.Any(x =>
//             x.Key.StartsWith(Constants.WEB_SOCKET_EXTENSION, StringComparison.OrdinalIgnoreCase));
//
//         if (hasWebSec is false)
//             return;
//         
//         var secWeb = headers.FirstOrDefault(x =>
//             x.Key.StartsWith(Constants.WEB_SOCKET_EXTENSION, StringComparison.OrdinalIgnoreCase));
//         var otherValues = secWeb.Value
//             .Where(x => string.Equals(x, "permessage-deflate", StringComparison.OrdinalIgnoreCase)).ToArray();
//         headers.Remove(secWeb.Key);
//         headers[Constants.WEB_SOCKET_EXTENSION] = otherValues;
//     }
// }