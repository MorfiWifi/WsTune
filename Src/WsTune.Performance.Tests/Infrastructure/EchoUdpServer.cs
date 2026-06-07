using System.Net;
using System.Net.Sockets;

namespace WsTune.Performance.Tests.Infrastructure;

public sealed class EchoUdpServer : IAsyncDisposable
{
    private readonly UdpClient _client;
    private readonly CancellationTokenSource _cts = new();
    private Task? _receiveLoop;

    public int Port { get; }

    private EchoUdpServer(int port)
    {
        Port = port;
        _client = new UdpClient(port);
    }

    public static EchoUdpServer Start(int? port = null)
    {
        var p = port ?? WsTuneCommon.PortManagement.GetFreePort();
        var server = new EchoUdpServer(p);
        server._receiveLoop = Task.Run(() => server.ReceiveLoopAsync(server._cts.Token));
        return server;
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await _client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                await _client.SendAsync(result.Buffer, result.RemoteEndPoint, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        _client.Close();
        if (_receiveLoop is not null)
        {
            try
            {
                await _receiveLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected.
            }
        }

        _cts.Dispose();
    }
}
