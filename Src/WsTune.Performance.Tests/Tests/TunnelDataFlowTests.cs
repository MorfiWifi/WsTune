using System.Net.Sockets;
using WsTune.Performance.Tests.Infrastructure;

namespace WsTune.Performance.Tests.Tests;

/// <summary>
/// Guards the data-plane wiring that the NoVNC -> Websockify -> TCP -> tunnel path depends on.
///
/// The listener splits traffic into a control plane (connect/disconnect, sent synchronously) and a
/// data plane (the actual bytes, queued through <c>PipelinedHubOutbound.EnqueueDataAsync</c>). The
/// data plane only drains once <c>StartPump</c> is called. If a host forgets that call — as the
/// loopback InternalHubService once did — connections still establish but no tunnel data flows,
/// which is exactly how a VNC session silently stops working.
/// </summary>
[Trait("Category", "Regression")]
public class TunnelDataFlowTests
{
    private static readonly byte[] Probe = "RFB 003.008\n"u8.ToArray();

    [Fact]
    public async Task TunnelData_FlowsEndToEnd_WhenPumpStarted()
    {
        await using var echo = EchoTcpServer.Start();
        await using var tunnel = new TunnelStackBootstrapper(startListenerPump: true);
        await tunnel.StartAsync(echo.Port);

        var echoed = await SendAndTryReadAsync(tunnel.ListenPort, Probe, TimeSpan.FromSeconds(10));

        Assert.NotNull(echoed);
        Assert.Equal(Probe, echoed);
    }

    [Fact]
    public async Task TunnelData_DoesNotFlow_WhenListenerPumpNotStarted()
    {
        // Reproduce the bug: pump never started, so queued data is never transmitted.
        await using var echo = EchoTcpServer.Start();
        await using var tunnel = new TunnelStackBootstrapper(startListenerPump: false);
        await tunnel.StartAsync(echo.Port);

        var echoed = await SendAndTryReadAsync(tunnel.ListenPort, Probe, TimeSpan.FromSeconds(3));

        // Connection establishes (control plane), but no bytes round-trip because the data-plane
        // pump never drains.
        Assert.Null(echoed);
    }

    /// <summary>
    /// Connects to the tunnel listen port, sends <paramref name="payload"/>, and waits up to
    /// <paramref name="timeout"/> for the echoed bytes. Returns the bytes received, or null on timeout.
    /// </summary>
    private static async Task<byte[]?> SendAndTryReadAsync(int listenPort, byte[] payload, TimeSpan timeout)
    {
        using var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync("127.0.0.1", listenPort).ConfigureAwait(false);

        // Let the control-plane ForwardConnection reach the server and open the echo connection.
        await Task.Delay(300).ConfigureAwait(false);

        var stream = client.GetStream();
        await stream.WriteAsync(payload).ConfigureAwait(false);

        using var cts = new CancellationTokenSource(timeout);
        var buffer = new byte[payload.Length];
        var received = 0;

        try
        {
            while (received < payload.Length)
            {
                var read = await stream
                    .ReadAsync(buffer.AsMemory(received, payload.Length - received), cts.Token)
                    .ConfigureAwait(false);

                if (read == 0)
                    break;

                received += read;
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        return received == payload.Length ? buffer : null;
    }
}
