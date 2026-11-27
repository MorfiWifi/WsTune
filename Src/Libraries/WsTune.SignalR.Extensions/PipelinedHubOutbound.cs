using Microsoft.AspNetCore.SignalR.Client;
using WsTuneCommon;
using WsTuneCommon.Models;

namespace WsTune.SignalR.Extensions;

/// <summary>
/// Queues data-plane SignalR invocations so TCP read loops are not blocked on hub RTT.
/// Control-plane calls (connection open/close, ping) stay synchronous.
/// </summary>
public sealed class PipelinedHubOutbound : IHubOutbounds, IAsyncDisposable
{
    private readonly BasicHubOutbound _inner = new();
    private readonly TunnelSendPump _pump = new();
    private CancellationToken _pumpCancellationToken;
    private bool _pumpConfigured;

    public PipelinedHubOutbound()
    {
        // A full backlog means the connection is not draining; ask the host to reset just that
        // TCP connection (so the app reconnects) instead of buffering without bound.
        _pump.OnOverflow = id =>
        {
            var reset = ConnectionReset;
            if (reset is not null)
                _ = reset(id);
        };
    }

    /// <summary>Max queued bytes before the offending connection is reset. See <see cref="TunnelSendPump"/>.</summary>
    public long MaxQueuedBytes
    {
        get => _pump.MaxQueuedBytes;
        set => _pump.MaxQueuedBytes = value;
    }

    /// <summary>Resets a single connection (both sockets) when its backlog overflows.</summary>
    public Func<Guid, Task>? ConnectionReset { get; set; }

    /// <summary>True when the connection still has undrained outbound bytes.</summary>
    public Func<Guid, bool>? IsDataPending { get; set; }

    public bool HasPendingData(Guid connectionId) => _pump.HasPendingData(connectionId);

    public event EventHandler? Reconnected;

    public void NotifyReconnected() => Reconnected?.Invoke(this, EventArgs.Empty);

    public void StartPump(CancellationToken cancellationToken)
    {
        _pumpCancellationToken = cancellationToken;
        _pumpConfigured = true;
        TryStartPump();
    }

    public void Initialize(HubConnection hubConnection)
    {
        _inner.Initialize(hubConnection);
        TryStartPump();
    }

    private void TryStartPump()
    {
        if (!_pumpConfigured)
            return;

        // The pump token flows into the inner send so a blocked (waiting-for-reconnect) send is
        // cancelled on shutdown instead of leaking. The inner outbound retries losslessly.
        _pump.Start(
            async (item, ct) => await _inner.SendAsync(item.Method, item.Packet, ct).ConfigureAwait(false),
            _pumpCancellationToken);
    }

    public ValueTask EnqueueDataAsync(string method, DataPacket packet, CancellationToken cancellationToken = default)
        => _pump.EnqueueAsync(method, packet, cancellationToken);

    public Task SendAsync(string method) => _inner.SendAsync(method);

    public Task SendAsync(string method, object arg1) => _inner.SendAsync(method, arg1);

    public Task SendAsync(string method, object arg1, object arg2) => _inner.SendAsync(method, arg1, arg2);

    public bool IsHubConnected() => _inner.IsHubConnected();

    public async ValueTask DisposeAsync()
    {
        await _pump.DisposeAsync().ConfigureAwait(false);
    }
}