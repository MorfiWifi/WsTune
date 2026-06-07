using WsTune.Performance.Tests.Evaluation;
using WsTune.Performance.Tests.Infrastructure;

namespace WsTune.Performance.Tests.Tests;

/// <summary>
/// UDP evaluation. Full Host/Listener/Server tunnel is TCP-only; these tests compare echo-direct vs UdpFwV3 forwarder.
/// </summary>
[Trait("Category", "Performance")]
public class UdpPerformanceEvaluationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task UdpEcho_DirectBaseline_EvaluatesThroughput()
    {
        await using var echo = EchoUdpServer.Start();

        const int datagramCount = 200;
        const int datagramSize = 512;

        var result = await UdpTransferRunner.RunDatagramTransferAsync(
            "Direct",
            "UDP client to echo (no forwarder)",
            echo.Port,
            datagramCount,
            datagramSize);

        Assert.True(result.DataVerified);
        PerformanceReporter.Write(output, result);
    }

    [Fact]
    public async Task DirectUdp_Forwarder_EvaluatesThroughput()
    {
        await using var echo = EchoUdpServer.Start();
        await using var forwarder = new DirectUdpForwarder();
        await forwarder.StartAsync(echo.Port);

        const int datagramCount = 200;
        const int datagramSize = 512;

        var result = await UdpTransferRunner.RunDatagramTransferAsync(
            "Direct",
            "UDP via UdpFwV3 component forwarder",
            forwarder.ListenPort,
            datagramCount,
            datagramSize);

        Assert.True(result.DataVerified);
        PerformanceReporter.Write(output, result);
        output.WriteLine("Note: end-to-end UDP through SignalR is not implemented in the net10 stack; use TCP tunnel comparison tests for full WsTune path.");
    }
}
