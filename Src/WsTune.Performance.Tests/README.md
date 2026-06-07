# WsTune.Performance.Tests

Unit-test style performance evaluation (no BenchmarkDotNet). Tests spin up real TCP/UDP echo servers and measure throughput and round-trip delay using `Stopwatch`.

## What is measured

| Scenario | Direct baseline | Full tunnel |
|----------|-----------------|-------------|
| Large transfer (8 MB) | `TcpFwV4` local forward | Host + Listener + Server + SignalR |
| Multi-connection (4 clients) | Same | Same |
| Round-trip latency | Echo over forwarder | Echo over full stack |
| UDP datagrams | `UdpFwV3` two-leg forward | N/A (TCP-only tunnel today) |

## Run

```bash
dotnet test Src/WsTune.Performance.Tests/WsTune.Performance.Tests.csproj
```

Performance comparisons (with insight in output):

```bash
dotnet test Src/WsTune.Performance.Tests --filter "Category=Performance"
```

View detailed metrics in the test runner **Standard Output** (throughput MB/s, avg/min/max RTT ms, tunnel vs direct retention %).

## Interpreting output

- **Throughput retention %** — tunnel MB/s divided by direct MB/s on loopback (100% = no overhead; typical tunnel values are lower).
- **Latency overhead ms** — extra average round-trip time vs direct TCP forwarder.
- Tests assert **data integrity** (payload echo verified); they do not fail on slow hardware, but comparison tests print guidance lines.
