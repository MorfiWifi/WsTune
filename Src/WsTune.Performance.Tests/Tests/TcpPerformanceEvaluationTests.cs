using WsTune.Performance.Tests.Evaluation;
using WsTune.Performance.Tests.Infrastructure;

namespace WsTune.Performance.Tests.Tests;

[Trait("Category", "Performance")]
public class TcpPerformanceEvaluationTests
{
    private const int LargeTransferBytes = 2 * 1024 * 1024;
    private const int ChunkSize = 64 * 1024;
    private const int MultiConnectionCount = 4;
    private const int MultiConnectionTotalBytes = 4 * 1024 * 1024;
    private const int LatencySamples = 40;
    private const int LatencyPayloadBytes = 512;

    [Fact]
    public async Task DirectTcp_LargeTransfer_EvaluatesThroughput()
    {
        await using var echo = EchoTcpServer.Start();
        await using var direct = new DirectTcpForwarder();
        await direct.StartAsync(echo.Port);

        var result = await TcpTransferRunner.RunBulkTransferAsync(
            "Direct",
            "Large transfer (single connection)",
            direct.ListenPort,
            LargeTransferBytes,
            ChunkSize,
            connectionCount: 1);

        Assert.True(result.DataVerified);
        Assert.True(result.MegabytesPerSecond > 0);
    }

    [Fact]
    public async Task TunnelTcp_LargeTransfer_EvaluatesThroughput()
    {
        await using var echo = EchoTcpServer.Start();
        await using var tunnel = new TunnelStackBootstrapper();
        await tunnel.StartAsync(echo.Port);

        var result = await TcpTransferRunner.RunBulkTransferAsync(
            "Tunnel",
            "Large transfer (single connection)",
            tunnel.ListenPort,
            LargeTransferBytes,
            ChunkSize,
            connectionCount: 1);

        Assert.True(result.DataVerified);
        Assert.True(result.MegabytesPerSecond > 0);
    }

    [Fact]
    public async Task DirectTcp_MultiConnection_EvaluatesAggregateThroughput()
    {
        await using var echo = EchoTcpServer.Start();
        await using var direct = new DirectTcpForwarder();
        await direct.StartAsync(echo.Port);

        var result = await TcpTransferRunner.RunBulkTransferAsync(
            "Direct",
            $"Multi-connection ({MultiConnectionCount} clients)",
            direct.ListenPort,
            MultiConnectionTotalBytes,
            ChunkSize,
            MultiConnectionCount);

        Assert.True(result.DataVerified);
        Assert.Equal(MultiConnectionCount, result.ConnectionCount);
    }

    [Fact]
    public async Task TunnelTcp_MultiConnection_EvaluatesAggregateThroughput()
    {
        await using var echo = EchoTcpServer.Start();
        await using var tunnel = new TunnelStackBootstrapper();
        await tunnel.StartAsync(echo.Port);

        var result = await TcpTransferRunner.RunBulkTransferAsync(
            "Tunnel",
            $"Multi-connection ({MultiConnectionCount} clients)",
            tunnel.ListenPort,
            MultiConnectionTotalBytes,
            ChunkSize,
            MultiConnectionCount);

        Assert.True(result.DataVerified);
        Assert.Equal(MultiConnectionCount, result.ConnectionCount);
    }

    [Fact]
    public async Task DirectTcp_RoundTripLatency_EvaluatesAverageDelay()
    {
        await using var echo = EchoTcpServer.Start();
        await using var direct = new DirectTcpForwarder();
        await direct.StartAsync(echo.Port);

        var result = await TcpTransferRunner.MeasureRoundTripLatencyAsync(
            "Direct",
            "Echo round-trip",
            direct.ListenPort,
            LatencySamples,
            LatencyPayloadBytes);

        Assert.True(result.AverageRoundTripMs > 0);
        Assert.Equal(LatencySamples, result.RoundTripSamples);
    }

    [Fact]
    public async Task TunnelTcp_RoundTripLatency_EvaluatesAverageDelay()
    {
        await using var echo = EchoTcpServer.Start();
        await using var tunnel = new TunnelStackBootstrapper();
        await tunnel.StartAsync(echo.Port);

        var result = await TcpTransferRunner.MeasureRoundTripLatencyAsync(
            "Tunnel",
            "Echo round-trip",
            tunnel.ListenPort,
            LatencySamples,
            LatencyPayloadBytes);

        Assert.True(result.AverageRoundTripMs > 0);
        Assert.Equal(LatencySamples, result.RoundTripSamples);
    }
}
