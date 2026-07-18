using Microsoft.AspNetCore.SignalR.Client;
using WsTune.SignalR.Extensions;

namespace WsTune.Performance.Tests.Tests;

/// <summary>
/// Regression guard for the periodic tunnel-disconnect bug.
///
/// Root cause (proven by frame-level capture): SignalR's ServerTimeout is WALL-CLOCK based.
/// The default 30s (only 2x the 15s host keep-alive) is trivially tripped by a forward
/// clock jump — on Docker Desktop/WSL2 the container clock was observed to jump +208s at
/// once, and NTP resyncs do it in production — which SignalR misreads as "no message for
/// 30s" and fires a FALSE disconnect. The reconnect then drops in-flight tunnel packets
/// (<see cref="BasicHubOutbound"/>) and desyncs the tunneled TCP stream. The default
/// reconnect policy also gave up after ~42s.
///
/// Fix: ServerTimeout is set very high so realistic clock jumps / jitter cannot trip it
/// (real drops are caught by the transport layer + FastInfiniteRetryPolicy, not this timer).
/// These tests fail if anyone lowers ServerTimeout back into the jump-vulnerable range or
/// reverts to a give-up reconnect policy.
/// </summary>
[Trait("Category", "Unit")]
public class BeatHubReconnectConfigTests
{
    // The host (THub) sends keep-alives on this cadence — see WsTuneCli.Host RegisterServices.
    private static readonly TimeSpan HostKeepAliveInterval = TimeSpan.FromSeconds(15);

    // Must comfortably exceed the largest clock jump we observed (+208s) so a jump can't
    // masquerade as server silence.
    private static readonly TimeSpan MinSafeServerTimeout = TimeSpan.FromMinutes(5);

    [Fact]
    public void Defaults_ServerTimeout_SurvivesClockJumps()
    {
        var options = new BeatHubOptions();

        Assert.True(options.ServerTimeout >= MinSafeServerTimeout,
            $"ServerTimeout {options.ServerTimeout} must be >= {MinSafeServerTimeout} so a wall-clock " +
            "jump / long GC / NTP resync cannot trip a false 'Server timeout' disconnect.");

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
