using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WsTune.SignalR.Extensions;
using WsTuneCli.Host;
using WsTuneCli.Listener.Extensions;
using WsTuneCli.Listener.Transport;
using WsTuneCli.Server.Transport;
using WsTuneCommon.Implementation;
using WsTuneCommon.Interfaces;
using WsTuneCommon.Models;

namespace WsTune.Performance.Tests.Infrastructure;

/// <summary>Host + Listener + Server over loopback SignalR (full tunnel path).</summary>
public sealed class TunnelStackBootstrapper : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private WebApplication? _hostApp;
    private Task? _hostRunTask;
    private Task? _listenerHubTask;
    private Task? _serverHubTask;
    private readonly List<Task> _forwarderTasks = [];
    private readonly Dictionary<string, IFwV4> _listenerForwarders = new();

    public int ListenPort { get; private set; }
    public int HostPort { get; private set; }
    public string ListenerIdentity { get; } = "perf-listener";
    public string ServerIdentity { get; } = "perf-server";

    public async Task StartAsync(int echoPort, CancellationToken cancellationToken = default)
    {
        HostPort = WsTuneCommon.PortManagement.GetFreePort();
        ListenPort = WsTuneCommon.PortManagement.GetFreePort();

        var hubPath = "/perf-hub";
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://127.0.0.1:{HostPort}");
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        var appSettings = Program.RegisterServices(builder);
        appSettings.SignalREndpoint = hubPath;
        appSettings.WebSockifyEndpoint = "/ws";

        _hostApp = builder.Build();
        Program.SetAppUsings(_hostApp, appSettings);
        _hostRunTask = _hostApp.RunAsync(_cts.Token);

        await WsTuneCommon.PortManagement.WaitUntilPortOpen(HostPort, cancellationToken).ConfigureAwait(false);

        var hubUrl = $"http://127.0.0.1:{HostPort}{hubPath}";

        await StartServerAsync(hubUrl, echoPort, cancellationToken).ConfigureAwait(false);
        await StartListenerAsync(hubUrl, echoPort, cancellationToken).ConfigureAwait(false);

        await WsTuneCommon.PortManagement.WaitUntilPortOpen(ListenPort, cancellationToken).ConfigureAwait(false);
        await Task.Delay(2000, cancellationToken).ConfigureAwait(false);
    }

    private async Task StartServerAsync(string hubUrl, int echoPort, CancellationToken cancellationToken)
    {
        var hubOutbounds = new PipelinedHubOutbound();
        var appSettings = new AppSettings
        {
            Identity = ServerIdentity,
            SignalREndpoint = hubUrl,
            WebSockifyEndpoint = "/ws",
            Configs = [],
            WhiteList =
            [
                new TunnelConfigDto
                {
                    Protocol = "TCP",
                    TargetHost = "127.0.0.1",
                    TargetPort = echoPort
                }
            ]
        };

        var hubInbound = new SeverHubInbounds(appSettings, hubOutbounds, _cts.Token);
        var options = WsTuneCli.Server.Transport.TransportHostService.GenerateHubOptions(hubInbound, hubOutbounds,
            $"{hubUrl}?identity={ServerIdentity}");
        var beatHub = new BeatHub(options, NullLogger<BeatHub>.Instance);

        hubOutbounds.StartPump(_cts.Token);
        _serverHubTask = beatHub.Start(_cts.Token);

        await WaitForHubAsync(hubOutbounds, cancellationToken).ConfigureAwait(false);
    }

    private async Task StartListenerAsync(string hubUrl, int echoPort, CancellationToken cancellationToken)
    {
        var hubOutbounds = new PipelinedHubOutbound();
        var tunnelConfig = new TunnelConfigDto
        {
            Name = "perf-tunnel",
            Protocol = "TCP",
            ListenPort = ListenPort,
            TargetHost = "127.0.0.1",
            TargetPort = echoPort,
            Destination = ServerIdentity
        };

        var appSettings = new AppSettings
        {
            Identity = ListenerIdentity,
            SignalREndpoint = hubUrl,
            WebSockifyEndpoint = "/ws",
            Configs = [tunnelConfig]
        };

        var hubInbound = new ListenerHubInbounds(_listenerForwarders, _cts.Token);
        var options = WsTuneCli.Listener.Transport.TransportHostService.GenerateHubOptions(hubInbound, hubOutbounds,
            $"{hubUrl}?identity={ListenerIdentity}");
        var beatHub = new BeatHub(options, NullLogger<BeatHub>.Instance);

        hubOutbounds.StartPump(_cts.Token);
        _listenerHubTask = beatHub.Start(_cts.Token);

        await WaitForHubAsync(hubOutbounds, cancellationToken).ConfigureAwait(false);

        var listenerConfig = tunnelConfig.CreateTcpConfiguration(ListenerIdentity, hubOutbounds);
        var fw = new TcpFwV4(listenerConfig);
        _listenerForwarders[tunnelConfig.Name] = fw;
        var fwTask = fw.RunAsync(_cts.Token);
        _forwarderTasks.Add(fwTask);
    }

    private static async Task WaitForHubAsync(PipelinedHubOutbound outbound, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (outbound.IsHubConnected())
                return;

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException("SignalR client did not connect to the test host.");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var fw in _listenerForwarders.Values)
            fw.RequestStop();

        await _cts.CancelAsync().ConfigureAwait(false);

        if (_hostApp is not null)
            await _hostApp.StopAsync().ConfigureAwait(false);

        var tasks = new List<Task>();
        if (_hostRunTask is not null) tasks.Add(_hostRunTask);
        if (_listenerHubTask is not null) tasks.Add(_listenerHubTask);
        if (_serverHubTask is not null) tasks.Add(_serverHubTask);
        tasks.AddRange(_forwarderTasks);

        try
        {
            await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(3000)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }

        if (_hostApp is not null)
            await _hostApp.DisposeAsync().ConfigureAwait(false);

        _cts.Dispose();
    }
}
