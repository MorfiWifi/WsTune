using System.Net;
using System.Net.Sockets;

namespace WsTuneCommon.Extensions;

public static class Net45Extensions
{
    /// <summary>
    /// Provides an AcceptTcpClientAsync method that respects a CancellationToken.
    /// </summary>
    public static async Task<TcpClient> AcceptTcpClientAsync(this TcpListener listener,
        CancellationToken cancellationToken)
    {
        return await listener.AcceptTcpClientAsync();
    }


    // /// <summary>
    // /// Provides a ConnectAsync overload for IPEndPoint with CancellationToken.
    // /// </summary>
    public static async Task ConnectAsync(this TcpClient client, IPAddress address, int port,
        CancellationToken cancellationToken)
    {
        await client.ConnectAsync(address, port);
    }

    public static async Task<UdpReceiveResult> ReceiveAsync(this UdpClient client, CancellationToken cancellationToken)
    {
        return await client.ReceiveAsync();
    }
}