namespace WsTune.Performance.Tests.Evaluation;

/// <summary>Metrics from a single transfer or latency probe run.</summary>
public sealed class TransferEvaluation
{
    public required string Mode { get; init; }
    public required string Scenario { get; init; }
    public required string Protocol { get; init; }
    public long TotalBytes { get; init; }
    public TimeSpan Duration { get; init; }
    public int ConnectionCount { get; init; }
    public int RoundTripSamples { get; init; }
    public double AverageRoundTripMs { get; init; }
    public double MinRoundTripMs { get; init; }
    public double MaxRoundTripMs { get; init; }
    public bool DataVerified { get; init; }

    public double MegabytesPerSecond =>
        Duration.TotalSeconds > 0 ? TotalBytes / Duration.TotalSeconds / (1024.0 * 1024.0) : 0;

    public IEnumerable<string> ToReportLines()
    {
        yield return $"[{Mode}] {Scenario} ({Protocol})";
        yield return $"  Connections: {ConnectionCount}";
        yield return $"  Bytes: {TotalBytes:N0} in {Duration.TotalMilliseconds:F0} ms";
        yield return $"  Throughput: {MegabytesPerSecond:F2} MB/s";
        if (RoundTripSamples > 0)
        {
            yield return $"  Round-trip (ms): avg {AverageRoundTripMs:F2}, min {MinRoundTripMs:F2}, max {MaxRoundTripMs:F2} ({RoundTripSamples} samples)";
        }

        yield return $"  Data verified: {DataVerified}";
    }
}

public sealed class ComparisonInsight
{
    public required TransferEvaluation Direct { get; init; }
    public required TransferEvaluation Tunnel { get; init; }

    public double ThroughputRatio =>
        Direct.MegabytesPerSecond > 0 ? Tunnel.MegabytesPerSecond / Direct.MegabytesPerSecond : 0;

    public double ThroughputRetentionPercent => ThroughputRatio * 100.0;

    public double LatencyOverheadMs => Tunnel.AverageRoundTripMs - Direct.AverageRoundTripMs;

    public double LatencyOverheadPercent =>
        Direct.AverageRoundTripMs > 0 ? (LatencyOverheadMs / Direct.AverageRoundTripMs) * 100.0 : 0;

    public IEnumerable<string> ToReportLines()
    {
        yield return "=== Tunnel vs direct ===";
        yield return $"  Throughput retention: {ThroughputRetentionPercent:F1}% ({Tunnel.MegabytesPerSecond:F2} vs {Direct.MegabytesPerSecond:F2} MB/s)";
        yield return $"  Latency overhead: {LatencyOverheadMs:F2} ms ({LatencyOverheadPercent:F1}% over direct)";
        yield return ThroughputRetentionPercent >= 50
            ? "  Insight: tunnel keeps majority of direct throughput for this scenario."
            : ThroughputRetentionPercent >= 20
                ? "  Insight: tunnel adds significant overhead; expect slower large transfers."
                : "  Insight: tunnel throughput is heavily limited vs direct — check SignalR path and payload size.";
    }
}
