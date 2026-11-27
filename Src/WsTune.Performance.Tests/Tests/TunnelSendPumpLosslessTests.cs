using WsTuneCommon;
using WsTuneCommon.Models;

namespace WsTune.Performance.Tests.Tests;

/// <summary>
/// Guards the lossless data-plane guarantee: a transient send failure must NOT drop tunnel
/// bytes. The pump retries the same item until it is delivered, preserving FIFO order. Dropping
/// bytes here is what used to desync a tunneled TCP stream across a SignalR reconnect.
/// </summary>
[Trait("Category", "Unit")]
public class TunnelSendPumpLosslessTests
{
    [Fact]
    public async Task Pump_RetriesFailedSend_AndPreservesOrder()
    {
        using var cts = new CancellationTokenSource();
        var received = new List<string>();
        var attempts = 0;

        var pump = new TunnelSendPump();
        pump.Start(
            async (item, ct) =>
            {
                var attempt = Interlocked.Increment(ref attempts);

                // Fail the very first send once, like a hub that dropped mid-invocation.
                if (attempt == 1)
                {
                    await Task.Delay(10, ct).ConfigureAwait(false);
                    throw new IOException("simulated transient send failure");
                }

                lock (received)
                {
                    received.Add($"{item.Method}:{item.Packet.ConnectionId}");
                }

                await Task.CompletedTask.ConfigureAwait(false);
            },
            cts.Token);

        const int count = 5;
        var ids = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var id in ids)
        {
            await pump.EnqueueAsync("Forward", new DataPacket { ConnectionId = id });
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (true)
        {
            lock (received)
            {
                if (received.Count >= count)
                    break;
            }

            if (DateTime.UtcNow > deadline)
                break;

            await Task.Delay(20);
        }

        await pump.DisposeAsync();

        lock (received)
        {
            // Every item delivered, in order, despite the first send failing.
            Assert.Equal(
                ids.Select(id => $"Forward:{id}"),
                received);

            Assert.Equal(count, received.Count);
        }

        // 5 successful sends + at least 1 retry of the failed item.
        Assert.True(attempts >= count + 1, $"expected a retry, saw {attempts} attempts");
    }
}