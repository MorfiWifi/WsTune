using System.Collections.Concurrent;
using System.Threading.Channels;
using WsTuneCommon.Models;

namespace WsTuneCommon;

/// <summary>
/// Decouples TCP read loops from SignalR send latency while preserving per-pump FIFO order.
///
/// The pump is lossless: if a send fails (for example the hub dropped mid-invocation) the SAME
/// item is retried until it is delivered or the pump is shut down. Bytes are never skipped,
/// which is what keeps a tunneled TCP stream intact across a reconnect.
///
/// It is also bounded: <see cref="MaxQueuedBytes"/> caps the backlog. When the cap is exceeded
/// the offending connection is reported via <see cref="OnOverflow"/> (the caller resets that
/// single TCP connection so the app reconnects) rather than buffering without limit.
/// </summary>
public sealed class TunnelSendPump : IAsyncDisposable
{
    // Small per-item bookkeeping overhead on top of the payload bytes.
    private const long PerItemOverhead = 64;

    private readonly Channel<TunnelSendItem> _channel = Channel.CreateBounded<TunnelSendItem>(
        new BoundedChannelOptions(512)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    private readonly ConcurrentDictionary<Guid, long> _pendingByConnection = new();
    private readonly CancellationTokenSource _disposeCts = new();
    private CancellationTokenSource? _linkedCts;
    private Task? _pumpTask;
    private Func<TunnelSendItem, CancellationToken, Task>? _sendAsync;

    private long _queuedBytes;
    private long _maxQueuedBytes = 16L * 1024 * 1024;

    /// <summary>Max bytes buffered before the connection is reset. &lt;= 0 means unlimited.</summary>
    public long MaxQueuedBytes
    {
        get => Interlocked.Read(ref _maxQueuedBytes);
        set => Interlocked.Exchange(ref _maxQueuedBytes, value);
    }

    /// <summary>Invoked with the ConnectionId whose backlog exceeded <see cref="MaxQueuedBytes"/>.</summary>
    public Action<Guid>? OnOverflow { get; set; }

    /// <summary>True when this connection still has undrained outbound bytes (used for stuck detection).</summary>
    public bool HasPendingData(Guid connectionId)
        => _pendingByConnection.TryGetValue(connectionId, out var bytes) && bytes > 0;

    public void Start(Func<TunnelSendItem, CancellationToken, Task> sendAsync, CancellationToken cancellationToken)
    {
        if (_pumpTask is not null)
            return;

        _sendAsync = sendAsync;
        _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
        _pumpTask = Task.Run(() => PumpAsync(_linkedCts.Token), _linkedCts.Token);
    }

    public async ValueTask EnqueueAsync(string method, DataPacket packet, CancellationToken cancellationToken = default)
    {
        var size = (long)packet.Data.Length + PerItemOverhead;
        var cap = MaxQueuedBytes;

        if (cap > 0 && Interlocked.Add(ref _queuedBytes, size) > cap)
        {
            Interlocked.Add(ref _queuedBytes, -size);
            try
            {
                OnOverflow?.Invoke(packet.ConnectionId);
            }
            catch
            {
                // A faulty callback must never break the send path.
            }

            return;
        }

        AddPending(packet.ConnectionId, size);
        try
        {
            await _channel.Writer.WriteAsync(new TunnelSendItem(method, packet), cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            Interlocked.Add(ref _queuedBytes, -size);
            RemovePending(packet.ConnectionId, size);
            throw;
        }
    }

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
                    var size = (long)item.Packet.Data.Length + PerItemOverhead;
                    var delivered = false;

                    // Retry the same item until it is delivered. A transient failure must not
                    // drop tunnel bytes, otherwise the peer's TCP stream desyncs.
                    while (true)
                    {
                        try
                        {
                            await _sendAsync(item, cancellationToken).ConfigureAwait(false);
                            delivered = true;
                            break;
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            break;
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
                                break;
                            }
                        }
                    }

                    // Count the item as pending until it is actually delivered, so a send that is
                    // blocked (retrying) still shows up for stuck detection.
                    Interlocked.Add(ref _queuedBytes, -size);
                    RemovePending(item.Packet.ConnectionId, size);

                    if (delivered == false)
                        return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    private void AddPending(Guid id, long size)
        => _pendingByConnection.AddOrUpdate(id, size, (_, existing) => existing + size);

    private void RemovePending(Guid id, long size)
    {
        if (!_pendingByConnection.TryGetValue(id, out var current))
            return;

        var next = current - size;
        if (next <= 0)
            _pendingByConnection.TryRemove(id, out _);
        else
            _pendingByConnection[id] = next;
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