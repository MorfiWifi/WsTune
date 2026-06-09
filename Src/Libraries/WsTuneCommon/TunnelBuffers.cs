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
    {
        client.NoDelay = true;
        try
        {
            var socket = client.Client;
            socket.SendBufferSize = 256 * 1024;
            socket.ReceiveBufferSize = 256 * 1024;
        }
        catch
        {
            // Socket may not be initialized yet on some platforms.
        }
    }
}
