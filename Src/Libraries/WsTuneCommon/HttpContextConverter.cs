// using System.Net;
// using System.Net.Http.Headers;
// using Microsoft.AspNetCore.Http;
// using Microsoft.AspNetCore.WebUtilities;
// using Microsoft.Extensions.Primitives;
// using WsTuneCommon.Extensions;
// using WsTuneCommon.Models;
// using Constants = WsTuneCommon.Constants;
//
// namespace WSTuneCommon;
//
// public static class HttpContextConverter
// {
//     private static readonly HashSet<string> RestrictedRequestHeaders = new(StringComparer.OrdinalIgnoreCase)
//     {
//       "Origin" ,   /*"Connection", "Keep-Alive",*/ "Transfer-Encoding", "Expect",
//         "Proxy-Authenticate", "Proxy-Authorization", "TE", "Trailer"
//     };
//
//     private static readonly HashSet<string> RestrictedResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
//     {
//         /*"Connection", "Keep-Alive",*/ "Transfer-Encoding",
//         "Proxy-Authenticate", "Proxy-Authorization", "Trailer", "TE"
//     };
//
//     /// <summary>
//     /// Cleans the headers in the HTTP request DTO before sending it to the destination server.
//     /// </summary>
//     private static void CleanHeaders(this HttpRequestDto request)
//     {
//         foreach (var header in RestrictedRequestHeaders)
//         {
//             request.Headers.Remove(header);
//         }
//     }
//
//     /// <summary>
//     /// Cleans the headers in the HTTP response DTO before returning it to the client.
//     /// </summary>
//     private static void CleanHeaders(this HttpResponseDto response)
//     {
//         foreach (var header in RestrictedResponseHeaders)
//         {
//             response.Headers.Remove(header);
//         }
//     }
//
//     public static async Task<HttpRequestDto> GetHttpRequestDtoAsync(string requestId , string baseUrl , HttpContext context)
//     {
//         var requestDto = new HttpRequestDto( requestId , baseUrl , context );
//         await requestDto.SetRequestBodyAsync(context);
//         requestDto.CleanHeaders();
//         return requestDto;
//     }
//
//     public static async Task<HttpResponseDto> GetHttpResponseDtoAsync(
//         HttpRequestMessage request,
//         string requestId,
//         HttpResponseMessage? response,
//         bool ignoreBody = false,
//         CancellationToken? cancellationToken = null,
//         bool forceCacheContent = true)
//     {
//         // Read the response body as raw bytes to preserve compression
//         var body = ignoreBody
//             ? []
//             : await response?.Content.ReadAsByteArrayAsync(cancellationToken ?? CancellationToken.None);
//
//         // change cache control for static content
//         if (response?.Content.Headers.ContentType is { MediaType: not null })
//         {
//             var mediaType = response.Content.Headers.ContentType.MediaType;
//
//             if (IsStaticContent(mediaType) && forceCacheContent)
//             {
//                 response.Headers.CacheControl = new CacheControlHeaderValue
//                 {
//                     Public = true,
//                     MaxAge = TimeSpan.FromDays(30) // Cache for 30 days
//                 };
//             }
//         }
//
//         // Combine headers from both Response.Headers and Content.Headers
//         var headers = response?.Headers.ToDict() ?? [];
//
//         if (response?.Content.Headers.ContentLength is not null)
//         {
//             foreach (var contentHeader in response.Content.Headers)
//             {
//                 headers[contentHeader.Key] = contentHeader.Value.ToArray();
//             }
//         }
//         
//         
//         //no chunked!
//         headers.Remove("Transfer-Encoding");
//         
//         
//         // >>> BEGIN: Replace full base URL with relative path in redirection
//         if (response is not null && (int)response.StatusCode >= 300 && (int)response.StatusCode < 400 &&
//             headers.TryGetValue("Location", out var locationValues) &&
//             locationValues.Length > 0 &&
//             Uri.TryCreate(locationValues[0], UriKind.Absolute, out var redirectUri) &&
//             request.RequestUri is not null)
//         {
//             var requestBaseUri = new Uri(request.RequestUri.GetLeftPart(UriPartial.Authority));
//             if (redirectUri.AbsoluteUri.StartsWith(requestBaseUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase))
//             {
//                 var relativePath = redirectUri.AbsoluteUri[requestBaseUri.AbsoluteUri.Length..];
//                 if (string.IsNullOrEmpty(relativePath))
//                 {
//                     relativePath = "/"; // fallback to root
//                 }
//
//                 headers["Location"] = [relativePath];
//             }
//         }
//         // <<< END: Replace full base URL with relative path in redirection
//
//         var result = new HttpResponseDto
//         {
//             RequestId = requestId,
//             StatusCode = (int) (response?.StatusCode ?? HttpStatusCode.BadGateway),
//             Headers = headers,
//             Body = body, // Send byte array directly
//             ContentType = response?.Content.Headers.ContentType?.ToString() ?? "application/octet-stream"
//         };
//
//         result.CleanHeaders();
//
//         return result;
//     }
//
//     public static HttpRequestMessage GetHttpRequestMessage(HttpRequestDto httpRequest)
//     {
//         // For Socket.IO, we need to preserve the original path exactly as received
//         string targetUrl = $"{httpRequest.BaseUrl}{httpRequest.Path}";
//         string destinationHost = httpRequest.BaseUrl
//             .Replace("wss://" , "").Replace("ws://", "")
//             .Replace("https://" , "").Replace("http://", "");
//
//         // Add query parameters if they exist
//         if (httpRequest.QueryParams is { Count: > 0 })
//         {
//             var queries = httpRequest.QueryParams.ToDictionary(x => x.Key, x => new StringValues(x.Value));
//             targetUrl = QueryHelpers.AddQueryString(targetUrl, queries);
//         }
//
//         var requestMessage = new HttpRequestMessage(new HttpMethod(httpRequest.Method), targetUrl);
//
//         // Add headers
//         if ((httpRequest.Body is { Length: > 0 } || httpRequest.Method != "GET") && httpRequest.ContentType != null)
//         {
//             var byteContent = new ByteArrayContent(httpRequest.Body);
//
//             // Set Content-Type header
//             byteContent.Headers.ContentType = MediaTypeHeaderValue.Parse(httpRequest.ContentType);
//
//             requestMessage.Content = byteContent;
//         }
//
//         //setting content will overwrite the content type
//         if (httpRequest.Headers == null) return requestMessage;
//
//         foreach (var header in httpRequest.Headers)
//         {
//             // Skip headers that might cause issues with HttpClient
//             if (
//                 !string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase)
//                 // &&
//                 // !string.Equals(header.Key, "Connection", StringComparison.OrdinalIgnoreCase)
//             )
//             {
//                 requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
//             }
//         }
//         
//         //JUST TESTING Headers 
//         // requestMessage.Headers.TryAddWithoutValidation("Host", httpRequest.Headers["X-Forwarded-Host"].ToArray());
//         // requestMessage.Headers.TryAddWithoutValidation("Origin", httpRequest.Headers["X-Forwarded-Host"].ToArray());
//         
//         requestMessage.Headers.TryAddWithoutValidation("Host", destinationHost);
//         requestMessage.Headers.TryAddWithoutValidation("Origin", destinationHost);
//         
//         return requestMessage;
//     }
//
//
//     /// <summary>
//     ///  Helper method to check if content is static
//     /// </summary>
//     private static bool IsStaticContent(string mediaType)
//     {
//         return mediaType switch
//         {
//             "text/javascript" or "application/javascript" or "application/x-javascript" => true, // JavaScript
//             "text/css" => true, // CSS
//             "image/png" or "image/jpeg" or "image/gif" or "image/webp" or "image/svg+xml" => true, // Images
//             "font/woff" or "font/woff2" or "application/font-woff" or "application/font-woff2" => true, // Fonts
//             "application/json" => false, // JSON should not be cached long-term
//             _ => false
//         };
//     }
// }