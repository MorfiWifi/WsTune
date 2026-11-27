using Microsoft.AspNetCore.SignalR.Client;

namespace WsTune.SignalR.Extensions;

/// <summary>
/// Sends invocations on the current <see cref="HubConnection"/>.
///
/// Unlike the previous "drop the call when not connected" behaviour, <c>SendAsync</c> now
/// WAITS for the connection to be (re)established before sending. That is what makes the
/// tunnel lossless across a SignalR reconnect: bytes are never silently discarded (dropping
/// them used to desync the tunneled TCP stream and permanently break the session). The wait
/// is bounded only by the caller's <see cref="CancellationToken"/>, so a shutdown still
/// unblocks promptly.
/// </summary>
public class BasicHubOutbound : IHubOutbounds
{
    protected HubConnection? Hub;

    // How often a blocked send re-checks the connection state while waiting for a reconnect.
    // Only paid while disconnected; a connected send returns immediately.
    private static readonly TimeSpan ReconnectPollInterval = TimeSpan.FromMilliseconds(25);

    public void Initialize(HubConnection hubConnection)
    {
        Hub = hubConnection;
    }

    public Task SendAsync(string method)
        => SendAsync(method, CancellationToken.None);

    public Task SendAsync(string method, object arg1)
        => SendAsync(method, arg1, CancellationToken.None);

    public Task SendAsync(string method, object arg1, object arg2)
    {
        return SendCoreAsync(method, arg1, arg2, CancellationToken.None);
    }

    // Cancellation-aware overload used by the data-plane pump so a shutdown can interrupt a
    // send that is blocked waiting for a reconnect.
    public Task SendAsync(string method, CancellationToken cancellationToken)
        => SendCoreAsync(method, null, null, cancellationToken);

    public Task SendAsync(string method, object arg1, CancellationToken cancellationToken)
        => SendCoreAsync(method, arg1, null, cancellationToken);

    private async Task SendCoreAsync(
        string method,
        object? arg1,
        object? arg2,
        CancellationToken cancellationToken)
    {
        await WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

        var hub = Hub;
        if (hub is null)
            return;

        if (arg2 is not null)
            await hub.SendAsync(method, arg1!, arg2).ConfigureAwait(false);
        else if (arg1 is not null)
            await hub.SendAsync(method, arg1).ConfigureAwait(false);
        else
            await hub.SendAsync(method).ConfigureAwait(false);
    }

    /// <summary>
    /// Blocks until the hub is connected (initially or after a reconnect), or until
    /// <paramref name="cancellationToken"/> is cancelled. Returns immediately if there is no
    /// hub to send on, preserving the legacy no-op behaviour for that edge case.
    /// </summary>
    private async Task WaitForConnectionAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var hub = Hub;
            if (hub is null)
                return;
            if (hub.State == HubConnectionState.Connected)
                return;

            await Task.Delay(ReconnectPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    public bool IsHubConnected()
        => Hub is not null && Hub.State == HubConnectionState.Connected;
}