using System.Threading.Channels;
using WsTuneCommon.Models;

namespace WsTuneCommon;

/// <summary>
/// Decouples TCP read loops from SignalR send latency while preserving per-pump FIFO order.
///
/// The pump is lossless: if a send fails (for example the hub dropped mid-invocation) the SAME
/// item is retried until it is delivered or the pump is shut down. Bytes are never skipped,
/// which is what keeps a tunneled TCP stream intact across a reconnect.
/// </summary>
public sealed class TunnelSendPump : IAsyncDisposable
{
    private readonly Channel<TunnelSendItem> _channel = Channel.CreateBounded<TunnelSendItem>(
        new BoundedChannelOptions(512)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    private readonly CancellationTokenSource _disposeCts = new();
    private CancellationTokenSource? _linkedCts;
    private Task? _pumpTask;
    private Func<TunnelSendItem, CancellationToken, Task>? _sendAsync;

    public void Start(Func<TunnelSendItem, CancellationToken, Task> sendAsync, CancellationToken cancellationToken)
    {
        if (_pumpTask is not null)
            return;

        _sendAsync = sendAsync;
        _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
        _pumpTask = Task.Run(() => PumpAsync(_linkedCts.Token), _linkedCts.Token);
    }

    public ValueTask EnqueueAsync(string method, DataPacket packet, CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(new TunnelSendItem(method, packet), cancellationToken);

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        if (_sendAsync is null)
            return;

        // Drain loop preserves per-pump FIFO order without IAsyncEnumerable,
        // which is unavailable on netstandard2.0/net48.
        var reader = _channel.Reader;
        try
        {
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (reader.TryRead(out var item))
                {
                    // Retry the same item until it is delivered. A transient failure must not
                    // drop tunnel bytes, otherwise the peer's TCP stream desyncs.
                    while (true)
                    {
                        try
                        {
                            await _sendAsync(item, cancellationToken).ConfigureAwait(false);
                            break;
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            return;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Tunnel send pump error: {ex.Message} — retrying");
                            try
                            {
                                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException)
                            {
                                return;
                            }
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposeCts.Cancel();
        _channel.Writer.TryComplete();
        if (_pumpTask is not null)
        {
            try
            {
                await _pumpTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }
    }
}

public readonly record struct TunnelSendItem(string Method, DataPacket Packet);