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

    public BeatHub(BeatHubOptions options, ILogger logger)
    {
        _options = options;
        _inbounds = options.Inbound;
        _outbounds = options.Outbounds;
        _logger = logger;

        ThrowExceptionOnBadParam();
    }

    private void ThrowExceptionOnBadParam()
    {
        if (_inbounds is null)
            throw new NullReferenceException(nameof(_inbounds));

        if (_outbounds is null)
            throw new NullReferenceException(nameof(_outbounds));
    }

    public async Task Start(CancellationToken cancellationToken = default)
    {
        _cancellationToken = cancellationToken;
        while (cancellationToken.IsCancellationRequested == false)
        {
            _logger.LogDebug($"Starting beating hub Connecting to {_options.Url}");

            await HealthCheckAndReconnect();

            await Task.Delay(_options.Delay + _random.Next(10, 500), cancellationToken);

            _logger.LogDebug($"Reconnecting to  {_options.Url}");
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
            _logger.LogDebug($"building hub connection {_options.Url}");

            _hubConnection = BuildHubConnection();

            try
            {
                await _hubConnection.StartAsync(_cancellationToken ?? CancellationToken.None);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "exception starting hub connection");
            }
        }
    }

    private async Task TryHealthCheck()
    {
        if (_hubConnection?.State == HubConnectionState.Connected)
        {
            _logger.LogDebug($"Health checking  {_options.Url}");

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
            _logger.LogDebug($"disposing connection {_options.Url}");

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
            .WithUrl(_options.Url)
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