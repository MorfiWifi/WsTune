using WsTune.Performance.Tests.Evaluation;
using WsTune.Performance.Tests.Infrastructure;

namespace WsTune.Performance.Tests.Tests;

/// <summary>Side-by-side tunnel vs direct with human-readable insight in test output.</summary>
[Trait("Category", "Performance")]
public class TcpTunnelComparisonTests(ITestOutputHelper output)
{
    private const int CompareTransferBytes = 2 * 1024 * 1024;
    private const int ChunkSize = 32 * 1024;
    private const int LatencySamples = 30;
    private const int LatencyPayloadBytes = 256;

    [Fact]
    public async Task Compare_LargeTransfer_TunnelVsDirect_ReportsInsight()
    {
        await using var echo = EchoTcpServer.Start();

        await using var direct = new DirectTcpForwarder();
        await direct.StartAsync(echo.Port);

        await using var tunnel = new TunnelStackBootstrapper();
        await tunnel.StartAsync(echo.Port);

        var directResult = await TcpTransferRunner.RunBulkTransferAsync(
            "Direct",
            "Large transfer",
            direct.ListenPort,
            CompareTransferBytes,
            ChunkSize,
            1);

        var tunnelResult = await TcpTransferRunner.RunBulkTransferAsync(
            "Tunnel",
            "Large transfer",
            tunnel.ListenPort,
            CompareTransferBytes,
            ChunkSize,
            1);

        Assert.True(directResult.DataVerified);
        Assert.True(tunnelResult.DataVerified);

        var insight = new ComparisonInsight { Direct = directResult, Tunnel = tunnelResult };
        PerformanceReporter.WriteComparison(output, insight);

        Assert.True(insight.Tunnel.MegabytesPerSecond > 0, "Tunnel should move data end-to-end.");
    }

    [Fact]
    public async Task Compare_MultiConnection_TunnelVsDirect_ReportsInsight()
    {
        await using var echo = EchoTcpServer.Start();

        await using var direct = new DirectTcpForwarder();
        await direct.StartAsync(echo.Port);

        await using var tunnel = new TunnelStackBootstrapper();
        await tunnel.StartAsync(echo.Port);

        const int connections = 3;
        const int totalBytes = 3 * 1024 * 1024;

        var directResult = await TcpTransferRunner.RunBulkTransferAsync(
            "Direct",
            "Multi-connection",
            direct.ListenPort,
            totalBytes,
            ChunkSize,
            connections);

        var tunnelResult = await TcpTransferRunner.RunBulkTransferAsync(
            "Tunnel",
            "Multi-connection",
            tunnel.ListenPort,
            totalBytes,
            ChunkSize,
            connections);

        Assert.True(directResult.DataVerified);
        Assert.True(tunnelResult.DataVerified);

        var insight = new ComparisonInsight { Direct = directResult, Tunnel = tunnelResult };
        PerformanceReporter.WriteComparison(output, insight);
    }

    [Fact]
    public async Task Compare_RoundTripLatency_TunnelVsDirect_ReportsInsight()
    {
        await using var echo = EchoTcpServer.Start();

        await using var direct = new DirectTcpForwarder();
        await direct.StartAsync(echo.Port);

        await using var tunnel = new TunnelStackBootstrapper();
        await tunnel.StartAsync(echo.Port);

        var directResult = await TcpTransferRunner.MeasureRoundTripLatencyAsync(
            "Direct",
            "Round-trip latency",
            direct.ListenPort,
            LatencySamples,
            LatencyPayloadBytes);

        var tunnelResult = await TcpTransferRunner.MeasureRoundTripLatencyAsync(
            "Tunnel",
            "Round-trip latency",
            tunnel.ListenPort,
            LatencySamples,
            LatencyPayloadBytes);

        var insight = new ComparisonInsight { Direct = directResult, Tunnel = tunnelResult };
        PerformanceReporter.WriteComparison(output, insight);

        Assert.True(tunnelResult.AverageRoundTripMs >= directResult.AverageRoundTripMs * 0.5,
            "Tunnel RTT should be measurable (typically higher than direct on loopback).");
    }
}
