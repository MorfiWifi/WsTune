using WsTuneCommon;
using WsTuneCommon.Models;

namespace WsTune.Performance.Tests.Tests;

/// <summary>
/// Guards the bounded-buffer / reset policy: the data-plane pump must cap its backlog and report
/// the offending connection (so the host resets it) instead of buffering without limit, and must
/// report undrained bytes for stuck detection.
/// </summary>
[Trait("Category", "Unit")]
public class TunnelSendPumpBufferTests
{
    [Fact]
    public async Task Enqueue_OverCap_DropsAndReportsConnection()
    {
        var pump = new TunnelSendPump { MaxQueuedBytes = 1024 };
        Guid? overflow = null;
        pump.OnOverflow = id => overflow = id;

        // Pump not started, so nothing drains.
        var id = Guid.NewGuid();
        await pump.EnqueueAsync("Forward", new DataPacket { ConnectionId = id, Data = new byte[2048] });

        Assert.Equal(id, overflow);
        Assert.False(pump.HasPendingData(id));

        await pump.DisposeAsync();
    }

    [Fact]
    public async Task HasPendingData_TrueWhileBlockedThenFalseAfterDelivery()
    {
        using var cts = new CancellationTokenSource();
        var pump = new TunnelSendPump { MaxQueuedBytes = 0 }; // unlimited

        var id = Guid.NewGuid();
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var delivered = new TaskCompletionSource();

        pump.Start(
            async (item, ct) =>
            {
                started.SetResult();
                await release.Task.WaitAsync(ct);
                delivered.SetResult();
            },
            cts.Token);

        await pump.EnqueueAsync("Forward", new DataPacket { ConnectionId = id, Data = new byte[100] });

        // Item is dequeued and the send is blocked (retrying) — still counted as pending.
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(pump.HasPendingData(id));

        release.SetResult();
        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (pump.HasPendingData(id) && DateTime.UtcNow < deadline)
            await Task.Delay(20);

        Assert.False(pump.HasPendingData(id));

        await pump.DisposeAsync();
    }
}