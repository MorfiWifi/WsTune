using WsTuneCommon.Implementation;
using WsTuneCommon.Models;

namespace WsTune.Performance.Tests.Infrastructure;

/// <summary>Two-leg UdpFwV3 bridge (component baseline — UDP is not tunneled via SignalR in the net10 stack).</summary>
public sealed class DirectUdpForwarder : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private UdpFwV3? _listener;
    private UdpFwV3? _server;
    private Task? _listenerTask;
    private Task? _serverTask;

    public int ListenPort { get; private set; }

    public async Task StartAsync(int echoPort, CancellationToken cancellationToken = default)
    {
        ListenPort = WsTuneCommon.PortManagement.GetFreePort();

        UdpFwV3? serverRef = null;
        UdpFwV3? listenerRef = null;

        Func<ForwardModel, Task> onListenerDataReceived = async context =>
            await serverRef!.SendDataToServer(context.Data, context.Length).ConfigureAwait(false);

        Func<ForwardModel, Task> onServerDataReceived = async context =>
            await listenerRef!.SendDataToListener(context.Data, context.Length).ConfigureAwait(false);

        serverRef = new UdpFwV3(null, onServerDataReceived, targetHost: "127.0.0.1", targetPort: echoPort);
        listenerRef = new UdpFwV3(onListenerDataReceived, null, listenPort: ListenPort);

        _server = serverRef;
        _listener = listenerRef;
        _listenerTask = _listener.RunAsync(_cts.Token);
        _serverTask = _server.RunAsync(_cts.Token);
        await Task.Delay(200, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        var tasks = new[] { _listenerTask, _serverTask }.Where(t => t is not null).Cast<Task>().ToArray();
        if (tasks.Length > 0)
            await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(1000)).ConfigureAwait(false);
        _cts.Dispose();
    }
}
