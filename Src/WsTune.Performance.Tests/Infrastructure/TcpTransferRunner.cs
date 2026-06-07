using System.Diagnostics;
using System.Net.Sockets;
using WsTune.Performance.Tests.Evaluation;

namespace WsTune.Performance.Tests.Infrastructure;

public static class TcpTransferRunner
{
    public static async Task<TransferEvaluation> RunBulkTransferAsync(
        string mode,
        string scenario,
        int listenPort,
        long totalBytes,
        int chunkSize,
        int connectionCount,
        CancellationToken cancellationToken = default)
    {
        var perConnection = totalBytes / connectionCount;
        var sw = Stopwatch.StartNew();

        var tasks = Enumerable.Range(0, connectionCount).Select(async connectionIndex =>
        {
            using var client = new TcpClient();
            client.NoDelay = true;
            await client.ConnectAsync("127.0.0.1", listenPort, cancellationToken).ConfigureAwait(false);
            await Task.Delay(150, cancellationToken).ConfigureAwait(false);
            var stream = client.GetStream();

            var chunk = PayloadFactory.CreatePatternBuffer(chunkSize, (byte)connectionIndex);
            long sent = 0;
            var receiveBuffer = new byte[chunkSize];
            var connectionOk = true;

            while (sent < perConnection)
            {
                var toSend = (int)Math.Min(chunkSize, perConnection - sent);
                await stream.WriteAsync(chunk.AsMemory(0, toSend), cancellationToken).ConfigureAwait(false);

                var received = 0;
                while (received < toSend)
                {
                    var read = await stream.ReadAsync(receiveBuffer.AsMemory(received, toSend - received), cancellationToken)
                        .ConfigureAwait(false);
                    if (read == 0)
                        throw new IOException("Echo closed before all bytes were received.");

                    received += read;
                }

                if (!PayloadFactory.BuffersEqual(chunk.AsSpan(0, toSend), receiveBuffer.AsSpan(0, toSend)))
                    connectionOk = false;

                sent += toSend;
            }

            return connectionOk;
        });

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        sw.Stop();

        return new TransferEvaluation
        {
            Mode = mode,
            Scenario = scenario,
            Protocol = "TCP",
            TotalBytes = totalBytes,
            Duration = sw.Elapsed,
            ConnectionCount = connectionCount,
            DataVerified = results.All(x => x)
        };
    }

    public static async Task<TransferEvaluation> MeasureRoundTripLatencyAsync(
        string mode,
        string scenario,
        int listenPort,
        int samples,
        int payloadSize,
        CancellationToken cancellationToken = default)
    {
        var payload = PayloadFactory.CreatePatternBuffer(payloadSize);
        var receiveBuffer = new byte[payloadSize];
        var rtts = new List<double>(samples);

        using var client = new TcpClient();
        client.NoDelay = true;
        await client.ConnectAsync("127.0.0.1", listenPort, cancellationToken).ConfigureAwait(false);
        await Task.Delay(150, cancellationToken).ConfigureAwait(false);
        var stream = client.GetStream();

        for (var i = 0; i < samples; i++)
        {
            payload[0] = (byte)i;
            var sw = Stopwatch.StartNew();
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);

            var received = 0;
            while (received < payloadSize)
            {
                var read = await stream.ReadAsync(receiveBuffer.AsMemory(received, payloadSize - received), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                    throw new IOException("Echo closed during latency sample.");

                received += read;
            }

            sw.Stop();
            rtts.Add(sw.Elapsed.TotalMilliseconds);
        }

        return new TransferEvaluation
        {
            Mode = mode,
            Scenario = scenario,
            Protocol = "TCP",
            TotalBytes = (long)samples * payloadSize,
            Duration = TimeSpan.FromMilliseconds(rtts.Sum()),
            ConnectionCount = 1,
            RoundTripSamples = samples,
            AverageRoundTripMs = rtts.Average(),
            MinRoundTripMs = rtts.Min(),
            MaxRoundTripMs = rtts.Max(),
            DataVerified = true
        };
    }
}
