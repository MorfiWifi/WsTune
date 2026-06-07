using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using WsTune.Performance.Tests.Evaluation;

namespace WsTune.Performance.Tests.Infrastructure;

public static class UdpTransferRunner
{
    public static async Task<TransferEvaluation> RunDatagramTransferAsync(
        string mode,
        string scenario,
        int listenPort,
        int datagramCount,
        int datagramSize,
        CancellationToken cancellationToken = default)
    {
        var payload = PayloadFactory.CreatePatternBuffer(datagramSize);
        var receiveBuffer = new byte[datagramSize];
        var endpoint = new IPEndPoint(IPAddress.Loopback, listenPort);
        var verified = true;
        var sw = Stopwatch.StartNew();

        using var client = new UdpClient(0);
        client.Client.ReceiveTimeout = 5000;
        for (var i = 0; i < datagramCount; i++)
        {
            payload[0] = (byte)i;
            await client.SendAsync(payload, endpoint, cancellationToken).ConfigureAwait(false);
            using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            receiveCts.CancelAfter(TimeSpan.FromSeconds(5));
            var result = await client.ReceiveAsync(receiveCts.Token).ConfigureAwait(false);
            if (result.Buffer.Length < datagramSize ||
                !PayloadFactory.BuffersEqual(payload, result.Buffer.AsSpan(0, datagramSize)))
            {
                verified = false;
            }
        }

        sw.Stop();

        return new TransferEvaluation
        {
            Mode = mode,
            Scenario = scenario,
            Protocol = "UDP",
            TotalBytes = (long)datagramCount * datagramSize,
            Duration = sw.Elapsed,
            ConnectionCount = 1,
            RoundTripSamples = datagramCount,
            AverageRoundTripMs = sw.Elapsed.TotalMilliseconds / datagramCount,
            MinRoundTripMs = 0,
            MaxRoundTripMs = 0,
            DataVerified = verified
        };
    }
}
