using System.Threading.Channels;
using WsTuneCommon.Models;

namespace WsTuneCommon;

/// <summary>
/// Decouples TCP read loops from SignalR send latency while preserving per-pump FIFO order.
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

    private Task? _pumpTask;
    private Func<TunnelSendItem, CancellationToken, Task>? _sendAsync;
    private CancellationToken _cancellationToken;

    public void Start(Func<TunnelSendItem, CancellationToken, Task> sendAsync, CancellationToken cancellationToken)
    {
        if (_pumpTask is not null)
            return;

        _sendAsync = sendAsync;
        _cancellationToken = cancellationToken;
        _pumpTask = Task.Run(() => PumpAsync(cancellationToken), cancellationToken);
    }

    public ValueTask EnqueueAsync(string method, DataPacket packet, CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(new TunnelSendItem(method, packet), cancellationToken);

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        if (_sendAsync is null)
            return;

        await foreach (var item in _channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                await _sendAsync(item, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Tunnel send pump error: {ex.Message}");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
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
