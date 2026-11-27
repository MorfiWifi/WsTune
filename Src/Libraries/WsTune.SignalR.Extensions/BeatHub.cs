using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.DependencyInjection;

namespace WsTune.SignalR.Extensions;

/// <summary>
/// Wrapper for SignalR hubConnection with auto reconnect after connection is Lost
/// SignalR be default will dispose connection after retry and fails.
/// will be replaced with new connection
/// </summary>
public class BeatHub : IAsyncDisposable
{
    private HubConnection? _hubConnection;
    private readonly Random _random = new();
    private readonly BeatHubOptions _options;
    private readonly IHubInbounds _inbounds;
    private readonly IHubOutbounds _outbounds;
    private readonly ILogger _logger;
    private CancellationToken? _cancellationToken;

    // Rendezvous endpoints to try in order (primary first, then fallbacks). A dead primary is
    // rotated out on the next connect attempt so the tunnel survives a single hub outage.
    private readonly string[] _urls;
    private int _urlIndex;

    public BeatHub(BeatHubOptions options, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _inbounds = options.Inbound
            ?? throw new ArgumentException("Inbound hub handlers must be configured.", nameof(options));
        _outbounds = options.Outbounds
            ?? throw new ArgumentException("Outbound hub handlers must be configured.", nameof(options));
        _logger = logger;
        _urls = BuildUrls(options);
    }

    private static string[] BuildUrls(BeatHubOptions options)
    {
        var urls = new List<string>();
        if (string.IsNullOrWhiteSpace(options.Url) == false)
            urls.Add(options.Url);

        foreach (var fallback in options.FallbackUrls)
        {
            if (string.IsNullOrWhiteSpace(fallback) == false && urls.Contains(fallback) == false)
                urls.Add(fallback);
        }

        // Keep at least the configured URL so Build() fails loudly if none was supplied.
        return urls.Count == 0 ? [options.Url] : urls.ToArray();
    }

    private string CurrentUrl => _urls[_urlIndex % _urls.Length];

    public async Task Start(CancellationToken cancellationToken = default)
    {
        _cancellationToken = cancellationToken;
        while (cancellationToken.IsCancellationRequested == false)
        {
            _logger.LogDebug($"Starting beating hub Connecting to {CurrentUrl}");

            await HealthCheckAndReconnect();

            await Task.Delay(_options.Delay + _random.Next(10, 500), cancellationToken);

            _logger.LogDebug($"Reconnecting to  {CurrentUrl}");
        }
    }

    private async Task HealthCheckAndReconnect()
    {
        await TryHealthCheck();

        await TryDisposeOnDisconnection();

        await TryBuildAndConnect();
    }

    private async Task TryBuildAndConnect()
    {
        if (_hubConnection == null || _hubConnection.State == HubConnectionState.Disconnected)
        {
            _logger.LogDebug($"building hub connection {CurrentUrl}");

            _hubConnection = BuildHubConnection();

            try
            {
                await _hubConnection.StartAsync(_cancellationToken ?? CancellationToken.None);

                // Fresh (re)connect: let the host replay any state that lives only on the peer
                // (e.g. the listener re-sends ForwardConnection for active local sockets).
                _outbounds.NotifyReconnected();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "exception starting hub connection");

                // Try the next rendezvous endpoint on the following iteration.
                if (_urls.Length > 1)
                {
                    _urlIndex = (_urlIndex + 1) % _urls.Length;
                    _logger.LogWarning("Failing over to rendezvous endpoint {Url}", CurrentUrl);
                }
            }
        }
    }

    private async Task TryHealthCheck()
    {
        if (_hubConnection?.State == HubConnectionState.Connected)
        {
            _logger.LogDebug($"Health checking  {CurrentUrl}");

            try
            {
                await _outbounds.SendAsync(_options.HeartBitFunctionName);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "exception calling heart-bit-function");
            }
        }
    }

    private async Task TryDisposeOnDisconnection()
    {
        if (_hubConnection?.State == HubConnectionState.Disconnected)
        {
            _logger.LogDebug($"disposing connection {CurrentUrl}");

            try
            {
                await _hubConnection.DisposeAsync();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "exception while disposing hub connection");
            }
        }
    }

    private HubConnection BuildHubConnection()
    {
        var connectionBuilder = new HubConnectionBuilder()
            .WithUrl(CurrentUrl)
            // Reconnect fast and forever. The SignalR default policy ([0,2,10,30]s then
            // gives up) leaves multi-second data gaps and abandons the connection after
            // ~42s, relying on the outer 60s rebuild loop.
            .WithAutomaticReconnect(new FastInfiniteRetryPolicy())
            .ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));

        if (_options.CustomConfigurationsFunc is not null)
        {
            connectionBuilder = _options.CustomConfigurationsFunc(connectionBuilder);
        }

        var hubConnection = connectionBuilder
            .Build();

        // Widen the keep-alive margin so a jittered/late server keep-alive does not trip a
        // false "Server timeout" disconnect (the root cause of periodic tunnel stalls).
        hubConnection.ServerTimeout = _options.ServerTimeout;
        hubConnection.KeepAliveInterval = _options.KeepAliveInterval;

        RegisterStatsLogger(hubConnection);

        //register inbound/out functions
        _inbounds.Register(hubConnection);
        _outbounds.Initialize(hubConnection);

        return hubConnection;
    }

    private void RegisterStatsLogger(HubConnection hubConnection)
    {
        hubConnection.Reconnected += s =>
        {
            _logger.LogDebug("Reconnected: {S}", s);

            // SignalR's own automatic reconnect succeeded: replay peer-side state too.
            _outbounds.NotifyReconnected();
            return Task.CompletedTask;
        };

        hubConnection.Reconnecting += (e) =>
        {
            _logger.LogDebug(e, "Reconnecting to SignalR server...");
            return Task.CompletedTask;
        };

        hubConnection.Closed += e =>
        {
            _logger.LogDebug(e, "Connection to SignalR server lost.");
            return Task.CompletedTask;
        };
    }

    public async Task StopAsync()
    {
        if (_hubConnection is not null)
            await _hubConnection.StopAsync(_cancellationToken ?? CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        if (_hubConnection is not null)
            await _hubConnection.DisposeAsync();
    }
}

/// <summary>
/// Reconnect immediately, then retry on a short fixed cadence forever. Unlike SignalR's
/// default policy this never gives up, so a tunnel client always recovers on its own.
/// </summary>
public sealed class FastInfiniteRetryPolicy : IRetryPolicy
{
    public TimeSpan? NextRetryDelay(RetryContext retryContext)
        => retryContext.PreviousRetryCount switch
        {
            0 => TimeSpan.Zero,
            < 5 => TimeSpan.FromSeconds(1),
            _ => TimeSpan.FromSeconds(2),
        };
}
