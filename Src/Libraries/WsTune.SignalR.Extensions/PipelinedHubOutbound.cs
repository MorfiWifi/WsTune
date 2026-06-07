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

        _pump.Start(async (item, ct) => await _inner.SendAsync(item.Method, item.Packet).ConfigureAwait(false), _pumpCancellationToken);
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
