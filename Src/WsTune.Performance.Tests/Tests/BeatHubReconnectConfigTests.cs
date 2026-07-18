using Microsoft.AspNetCore.SignalR.Client;
using WsTune.SignalR.Extensions;

namespace WsTune.Performance.Tests.Tests;

/// <summary>
/// Regression guard for the periodic tunnel-disconnect bug.
///
/// Root cause was that BeatHub left the SignalR client on its defaults: ServerTimeout
/// 30s against a host KeepAliveInterval of 15s (only a 2x margin), so a single jittered
/// keep-alive tripped a false "Server timeout" disconnect. During even a millisecond
/// reconnect, <see cref="BasicHubOutbound"/> drops in-flight tunnel packets and desyncs
/// the tunneled TCP stream. The default reconnect policy also gave up after ~42s.
///
/// These tests fail if anyone weakens the keep-alive margin or reverts to a
/// give-up reconnect policy.
/// </summary>
[Trait("Category", "Unit")]
public class BeatHubReconnectConfigTests
{
    // The host (THub) sends keep-alives on this cadence — see WsTuneCli.Host RegisterServices.
    private static readonly TimeSpan HostKeepAliveInterval = TimeSpan.FromSeconds(15);

    [Fact]
    public void Defaults_KeepAliveMargin_IsAtLeast_Triple_HostKeepAlive()
    {
        var options = new BeatHubOptions();

        // The client must tolerate several missed host keep-alives, not just two.
        // SignalR's default (30s) is exactly 2x — the value that caused the bug.
        Assert.True(
            options.ServerTimeout >= TimeSpan.FromSeconds(3 * HostKeepAliveInterval.TotalSeconds),
            $"ServerTimeout {options.ServerTimeout} must be >= 3x the host keep-alive " +
            $"({HostKeepAliveInterval}) to avoid false 'Server timeout' disconnects.");

        Assert.True(options.ServerTimeout > TimeSpan.FromSeconds(30),
            "ServerTimeout must be greater than SignalR's default 30s (the buggy value).");
    }

    [Fact]
    public void Defaults_ClientKeepAlive_IsFrequentEnough()
    {
        var options = new BeatHubOptions();

        // Client pings must be well within the host's ClientTimeoutInterval (60s).
        Assert.True(options.KeepAliveInterval <= TimeSpan.FromSeconds(20),
            $"KeepAliveInterval {options.KeepAliveInterval} must stay small so the host " +
            "does not time the client out.");
    }

    [Fact]
    public void FastInfiniteRetryPolicy_NeverGivesUp()
    {
        var policy = new FastInfiniteRetryPolicy();

        foreach (var count in new long[] { 0, 1, 4, 5, 50, 1000, 100_000 })
        {
            var delay = policy.NextRetryDelay(new RetryContext { PreviousRetryCount = count });
            Assert.True(delay.HasValue,
                $"Retry policy returned null at PreviousRetryCount={count} — it must never give up.");
        }
    }

    [Fact]
    public void FastInfiniteRetryPolicy_ReconnectsImmediatelyThenStaysShort()
    {
        var policy = new FastInfiniteRetryPolicy();

        // First attempt is immediate so a blip recovers before it can matter.
        Assert.Equal(TimeSpan.Zero, policy.NextRetryDelay(new RetryContext { PreviousRetryCount = 0 }));

        // Every subsequent delay is short (default policy jumped to 10s/30s).
        foreach (var count in new long[] { 1, 2, 5, 100 })
        {
            var delay = policy.NextRetryDelay(new RetryContext { PreviousRetryCount = count })!.Value;
            Assert.InRange(delay, TimeSpan.Zero, TimeSpan.FromSeconds(2));
        }
    }
}
