using System.Buffers;
using System.Net.Sockets;

namespace WsTuneCommon;

public static class TunnelBuffers
{
    public const int ReadBufferSize = 65536;

    /// <summary>
    /// Copies exactly <paramref name="length"/> bytes from a rented pool buffer into an owned array for transport.
    /// </summary>
    public static byte[] CopyFromRentedBuffer(byte[] rented, int length)
    {
#if NET6_0_OR_GREATER
        var data = GC.AllocateUninitializedArray<byte>(length);
#else
        var data = new byte[length];
#endif
        rented.AsSpan(0, length).CopyTo(data);
        return data;
    }

    public static void ReturnRentedBuffer(byte[] rented)
        => ArrayPool<byte>.Shared.Return(rented, clearArray: false);

    public static void ConfigureTcpClient(TcpClient client)
        => ConfigureTcpClient(client, 10, 5, 3);

    /// <summary>
    /// Applies the tunnel socket options. <paramref name="keepAliveTimeSeconds"/> /
    /// <paramref name="keepAliveIntervalSeconds"/> / <paramref name="keepAliveRetryCount"/> tune
    /// how fast a dead network peer is detected (defaults ~30s). Values &lt;= 0 keep the OS default.
    /// </summary>
    public static void ConfigureTcpClient(
        TcpClient client,
        int keepAliveTimeSeconds,
        int keepAliveIntervalSeconds,
        int keepAliveRetryCount)
    {
        client.NoDelay = true;
        try
        {
            var socket = client.Client;
            socket.SendBufferSize = 256 * 1024;
            socket.ReceiveBufferSize = 256 * 1024;

            // Enable TCP keepalive so a flow silently reaped by an idle NAT/firewall is
            // detected instead of hanging until the next write fails. Without this, a dead
            // peer can look "connected" for a very long time.
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
#if NET6_0_OR_GREATER
            if (keepAliveTimeSeconds > 0)
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, keepAliveTimeSeconds);
            if (keepAliveIntervalSeconds > 0)
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, keepAliveIntervalSeconds);
            if (keepAliveRetryCount > 0)
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, keepAliveRetryCount);
#endif
        }
        catch
        {
            // Socket may not be initialized yet on some platforms.
        }
    }
}
