using WsTuneCommon.Implementation;
using WsTuneCommon.Models;

namespace WsTune.Performance.Tests.Infrastructure;

/// <summary>Local TcpFwV4 bridge (baseline — no SignalR / Host).</summary>
public sealed class DirectTcpForwarder : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private TcpFwV4? _forwarder;
    private Task? _runTask;

    public int ListenPort { get; private set; }

    public async Task StartAsync(int echoPort, CancellationToken cancellationToken = default)
    {
        ListenPort = WsTuneCommon.PortManagement.GetFreePort();

        async Task OnListenerDataReceived(ForwardModelV4 context, CancellationToken ct) =>
            await context.Accessor.SendDataToServerAsync(context.ConnectionId, context.Data, context.Length, ct)
                .ConfigureAwait(false);

        async Task OnServerDataReceived(ForwardModelV4 context, CancellationToken ct) =>
            await context.Accessor.SendDataToListenerAsync(context.ConnectionId, context.Data, context.Length, ct)
                .ConfigureAwait(false);

        async Task OnClientConnected(ForwardModelV4 context, CancellationToken ct) =>
            await context.Accessor.OpenServerConnectionAsync(context.ConnectionId, ct).ConfigureAwait(false);

        var config = new TcpFw4Config
        {
            Name = "direct-test",
            ListenPort = ListenPort,
            TargetHost = "127.0.0.1",
            TargetPort = echoPort,
            EnableWatchdog = false,
            OnListenerDataReceived = OnListenerDataReceived,
            OnServerDataReceived = OnServerDataReceived,
            OnClientConnected = OnClientConnected
        };

        _forwarder = new TcpFwV4(config);
        _runTask = _forwarder.RunAsync(_cts.Token);

        await WsTuneCommon.PortManagement.WaitUntilPortOpen(ListenPort, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        _forwarder?.RequestStop();
        await _cts.CancelAsync().ConfigureAwait(false);
        if (_runTask is not null)
        {
            var done = await Task.WhenAny(_runTask, Task.Delay(2000)).ConfigureAwait(false);
            if (done == _runTask)
            {
                try
                {
                    await _runTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected.
                }
            }
        }

        _cts.Dispose();
    }
}
