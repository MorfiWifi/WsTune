using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Http;
using WsAllInOne;
using WsTuneCli.Listener.Extensions;
using WsTuneCommon.Implementation;
using WsTuneCommon.Interfaces;

namespace WebsockifySharp.Websockify;

/// <summary>
/// Middleware for handling WebSocket connections and proxying data between WebSocket and TCP.
/// </summary>
public sealed class WebsockifyMiddleware
{
    /// <summary>
    /// The recommended buffer size for reading and writing data.
    /// </summary>
    public const int RecommendedBufferSize = 1024 * 64;

    private readonly RequestDelegate _next;
    private readonly int _bufferSizeInBytes;

    private static async Task<TcpClient> ConnectToForwarderAsync(
        int port,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (true)
        {
            var client = new TcpClient();
            try
            {
                await client
                    .ConnectAsync("127.0.0.1", port, cancellationToken)
                    .ConfigureAwait(false);
                return client;
            }
            catch (SocketException)
            {
                client.Dispose();
                if (DateTime.UtcNow >= deadline)
                    throw;

                await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebsockifyMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="bufferSizeInBytes">The buffer size in bytes.</param>
    public WebsockifyMiddleware(
        RequestDelegate next,
        int bufferSizeInBytes = RecommendedBufferSize)
    {
        if (next == null)
            throw new ArgumentNullException(nameof(next));
        if (bufferSizeInBytes < 1024)
            bufferSizeInBytes = RecommendedBufferSize;

        _next = next;
        _bufferSizeInBytes = bufferSizeInBytes;
    }

    private static async Task ReceiveTask(
        NetworkStream networkStream, WebSocket webSocket, int bufferSize,
        ILogger logger, CancellationToken cancellationToken)
    {
        var read = 0;
        var buffer = new byte[bufferSize];

        try
        {
            while (true)
            {
                read = await networkStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;

                await webSocket.SendAsync(
                    new ArraySegment<byte>(buffer, 0, read),
                    WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException exception) when (
            exception.InnerException is SocketException
            {
                SocketErrorCode: SocketError.OperationAborted
            })
        {
        }
        catch (Exception thrownException)
        {
            logger.LogError(thrownException, $"{nameof(ReceiveTask)} method interrupted due to exception.");
        }
    }

    private static async Task SendTask(
        WebSocket webSocket, NetworkStream networkStream, TcpClient tcpClient,
        int bufferSize, ILogger logger, CancellationToken cancellationToken)
    {
        var read = default(WebSocketReceiveResult);
        var buffer = new byte[bufferSize];

        try
        {
            while (true)
            {
                read = await webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    cancellationToken).ConfigureAwait(false);

                if (read.CloseStatus.HasValue)
                    break;

                await networkStream.WriteAsync(buffer, 0, read.Count, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (WebSocketException) when (
            webSocket.State is WebSocketState.Aborted or WebSocketState.Closed)
        {
        }
        catch (Exception thrownException)
        {
            logger.LogError(thrownException, $"{nameof(SendTask)} method interrupted due to exception.");
        }

        var lastRead = read;
        await networkStream.DisposeAsync().ConfigureAwait(false);
        tcpClient.Close();

        if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            var closeStatus = lastRead?.CloseStatus ?? WebSocketCloseStatus.NormalClosure;
            try
            {
                await webSocket.CloseAsync(
                    closeStatus,
                    lastRead?.CloseStatusDescription ?? string.Empty,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (WebSocketException)
            {
            }
        }
    }

    /// <summary>
    /// Invokes the middleware.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        var cancellationToken = context.RequestAborted;
        var logger = context.RequestServices.GetRequiredService<ILogger<WebsockifyMiddleware>>();

        // Extract configId from query string
        string? configId = null;
        if (context.Request.Query.TryGetValue("configId", out var queryId))
        {
            configId = queryId.ToString();
        }

        if (string.IsNullOrEmpty(configId) || !Storage.Configs.TryGetValue(configId, out var config))
        {
            context.Response.StatusCode = 404;
            await _next(context);
            return;
        }


        var fwFound = Storage.fws.TryGetValue(configId, out var fw);
        if ((fwFound is false || fw is null) && Storage.hubOutbounds is not null)
        {
            var listenerConfig = config.CreateTcpConfiguration(Storage.ListenerIdentity , Storage.hubOutbounds);

            fw = new TcpFwV4(listenerConfig);

            Storage.fws[config.Name] = fw;
            _ = fw.RunAsync(CancellationToken.None);
        }


        if (context.WebSockets.IsWebSocketRequest)
        {
            var webSocket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);

            TcpClient tcpClient;
            try
            {
                tcpClient = await ConnectToForwarderAsync(
                        config.ListenPort,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to connect to target TCP server.");
                try
                {
                    await webSocket
                        .CloseAsync(
                            WebSocketCloseStatus.InternalServerError,
                            "Connection failed",
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // The request ended before the failure close frame could be sent.
                }
                catch (WebSocketException)
                {
                    // The peer disconnected while the connection failure was reported.
                }
                return;
            }

            using (tcpClient)
            {
                var networkStream = tcpClient.GetStream();
                var receiveTask = ReceiveTask(networkStream, webSocket, _bufferSizeInBytes, logger, cancellationToken);
                var sendTask = SendTask(webSocket, networkStream, tcpClient, _bufferSizeInBytes, logger, cancellationToken);
                await Task.WhenAll(receiveTask, sendTask).ConfigureAwait(false);
            }
        }
        else
        {
            context.Response.StatusCode = 400;
        }

        await _next(context);
    }
}
