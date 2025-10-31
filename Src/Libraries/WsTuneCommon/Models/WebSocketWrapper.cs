// using System.Net.WebSockets;
//
// namespace WsTuneCommon.Models;
//
// public class WebSocketWrapper
// {
//     public string ConnectionId { get; } // socket identifier
//     private readonly WebSocket _webSocket;
//     private readonly CancellationTokenSource _cancellationTokenSource;
//     private readonly CancellationToken _cancellationToken;
//     private const int BufferSize = 4096;
//
//     // Define event for message received
//     public event EventHandler<WebSocketMessageDto>? MessageReceived;
//     
//     // Add event for connection closed
//     public event EventHandler<WebSocketCloseEventDto>? ConnectionClosed;
//     
//     public WebSocketState State => _webSocket.State;
//     
//     public WebSocketWrapper(WebSocket webSocket , string connectionId)
//     {
//         ConnectionId = connectionId;
//         _cancellationTokenSource = new CancellationTokenSource();
//         _cancellationToken = _cancellationTokenSource.Token;
//         _webSocket = webSocket;
//     }
//     
//
//     public async Task CloseAsync(int CloseStatus, string CloseDescription)
//     {
//         try
//         {
//             if (_webSocket.State == WebSocketState.Open)
//             {
//                 await _webSocket.CloseAsync(
//                     (WebSocketCloseStatus)CloseStatus,
//                     CloseDescription,
//                     _cancellationToken);
//             }
//         }
//         catch (Exception)
//         {
//             // ignored
//         }
//         finally
//         {
//             // Always dispose and cancel
//             _webSocket.Dispose();
//             await _cancellationTokenSource.CancelAsync();
//             
//             // Trigger connection closed event
//             ConnectionClosed?.Invoke(this, new WebSocketCloseEventDto 
//             { 
//                 ConnectionId = ConnectionId, 
//                 Reason = CloseDescription 
//             });
//         }
//     }
//
//     public async Task SendAsync(ArraySegment<byte> data,
//         WebSocketMessageType messageType,
//         bool isEndOfMessage)
//     {
//         try
//         {
//             if (_webSocket.State == WebSocketState.Open)
//             {
//                 await _webSocket.SendAsync(
//                     data,
//                     messageType,
//                     isEndOfMessage,
//                     CancellationToken.None);
//             }
//         }
//         catch (Exception)
//         {
//             // ignored
//         }
//     }
//
//     public async Task StartListening()
//     {
//         var buffer = new byte[BufferSize];
//         WebSocketCloseStatus closeStatus = WebSocketCloseStatus.NormalClosure;
//         string closeStatusDescription = "Normal closure";
//
//         try
//         {
//             while (_webSocket.State == WebSocketState.Open && !_cancellationToken.IsCancellationRequested)
//             {
//                 var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cancellationToken);
//
//                 if (result.MessageType == WebSocketMessageType.Close)
//                 {
//                     closeStatus = result.CloseStatus ?? WebSocketCloseStatus.NormalClosure;
//                     closeStatusDescription = result.CloseStatusDescription ?? "Connection closed by client";
//                     break;
//                 }
//
//                 var message = new WebSocketMessageDto()
//                 {
//                     IsEndOfMessage = result.EndOfMessage,
//                     ConnectionId = ConnectionId,
//                     MessageType = result.MessageType,
//                     Data = buffer.Take(result.Count).ToArray(),
//                 };
//
//                 // Trigger event with received data
//                 MessageReceived?.Invoke(this, message);
//             }
//         }
//         catch (OperationCanceledException)
//         {
//             // Normal cancellation, don't need to handle specially
//         }
//         catch (WebSocketException wsEx)
//         {
//             // Console.WriteLine($"WebSocket error: {wsEx.Message}");
//             closeStatus = WebSocketCloseStatus.InternalServerError;
//             closeStatusDescription = $"WebSocket error: {wsEx.Message}";
//         }
//         catch (Exception ex)
//         {
//             // Console.WriteLine($"General error in WebSocket: {ex.Message}");
//             closeStatus = WebSocketCloseStatus.InternalServerError;
//             closeStatusDescription = $"General error: {ex.Message}";
//         }
//         finally
//         {
//             try
//             {
//                 // Try to close the connection gracefully if it's still open
//                 if (_webSocket.State == WebSocketState.Open)
//                 {
//                     await _webSocket.CloseAsync(
//                         closeStatus,
//                         closeStatusDescription,
//                         CancellationToken.None);
//                 }
//             }
//             catch
//             {
//                 // Ignore errors during cleanup close
//             }
//             
//             // Ensure resources are released
//             _webSocket.Dispose();
//             await _cancellationTokenSource.CancelAsync();
//             
//             // Trigger connection closed event
//             ConnectionClosed?.Invoke(this, new WebSocketCloseEventDto 
//             { 
//                 ConnectionId = ConnectionId, 
//                 Reason = closeStatusDescription 
//             });
//         }
//     }
// }