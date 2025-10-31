using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using WsTuneCommon.Interfaces;
using WsTuneCommon.Models;

#if NET48
using WsTuneCommon.Extensions;
#endif

namespace WsTuneCommon.Implementation;

public class TcpFwV4 : IFwV4
{
    #region Ctor

    private readonly int _listenPort;
    private readonly string _targetHost;
    private readonly int _targetPort;
    private readonly bool _enableWatchdog;

    private readonly TcpListener? _listener;

    private readonly Func<ForwardModelV4, CancellationToken, Task>? _onListenerDataReceived;
    private readonly Func<ForwardModelV4, CancellationToken, Task>? _onServerDataReceived;
    private readonly Func<ForwardModelV4, CancellationToken, Task>? _onClientConnected;
    private readonly Func<ForwardModelV4, CancellationToken, Task>? _onClientDisconnected;
    private readonly IPEndPoint _targetEndPoint;

    public TcpFwV4(
        TcpFw4Config config
    )
    {
        Name = config.Name;
        
        if (config.OnListenerDataReceived is null && config.OnServerDataReceived is null)
        {
            throw new ArgumentException("At least one of the data received handlers must be provided.");
        }

        _onListenerDataReceived = config.OnListenerDataReceived;
        _onServerDataReceived = config.OnServerDataReceived;
        _onClientConnected = config.OnClientConnected;
        _onClientDisconnected = config.OnClientDisconnected;
        _listenPort = config.ListenPort;
        _targetHost = config.TargetHost;
        _targetPort = config.TargetPort;
        _enableWatchdog = config.EnableWatchdog;

        if (config.OnListenerDataReceived is not null)
            _listener = new TcpListener(IPAddress.Any, _listenPort);

        _targetEndPoint = new IPEndPoint(IPAddress.Parse(_targetHost), _targetPort);
    }

    #endregion

    private readonly ConcurrentDictionary<Guid, TcpClient> _listenerConnections = new();
    private readonly ConcurrentDictionary<Guid, TcpClient> _serverConnections = new();
    private readonly ConcurrentDictionary<Guid, DateTime> _lastActivity = new();
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    public string Name { get; }

    public async Task SendDataToListenerAsync(Guid id, byte[] data, int length,
        CancellationToken cancellationToken)
    {
        var (found, stream) = GetListenerStream(id);
        if (found && stream is not null)
        {
            await stream.WriteAsync(data, 0, length, cancellationToken);
            _lastActivity[id] = DateTime.UtcNow;
        }
    }

    public async Task SendDataToServerAsync(Guid id, byte[] data, int length, CancellationToken cancellationToken)
    {
        var (found, stream) = GetServerStream(id);
        if (found && stream is not null)
        {
            await stream.WriteAsync(data, 0, length, cancellationToken);
            _lastActivity[id] = DateTime.UtcNow;
        }
    }

    public async Task OpenServerConnectionAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        var tcpClient = new TcpClient();
        tcpClient.NoDelay = true;
        // await tcpClient.ConnectAsync(_targetEndPoint.Address, _targetEndPoint.Port, cancellationToken);
        await tcpClient.ConnectAsync(_targetEndPoint.Address, _targetEndPoint.Port);
        _serverConnections[connectionId] = tcpClient;
        _lastActivity[connectionId] = DateTime.UtcNow;

        _ = Task.Run(async () =>
        {
            try
            {
                if (_onServerDataReceived is null)
                    throw new ArgumentException("expected server data receiver to start handler");

                var stream = tcpClient.GetStream();
                await HandleStreamAsync(connectionId, stream, _onServerDataReceived, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Ignored
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Server handler error: {ex.Message}");
            }
            finally
            {
                await CloseConnectionAsync(connectionId, cancellationToken);
            }
        }, cancellationToken);
    }

    public Task DisposeClientAsync(Guid connectionId)
    {
        if (_listenerConnections.TryRemove(connectionId, out var listenerClient))
        {
            try
            {
                listenerClient?.Close();
            }
            catch
            {
                //ignore
            }
        }

        if (_serverConnections.TryRemove(connectionId, out var serverClient))
        {
            try
            {
                serverClient?.Close();
            }
            catch
            {
                //ignore
            }
        }

        _lastActivity.TryRemove(connectionId, out _);
        return Task.CompletedTask;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"TCP forwarder running: {_listenPort} -> {_targetHost}:{_targetPort}");

        Task listenerTask = Task.CompletedTask;
        if (_onListenerDataReceived != null)
        {
            listenerTask = Task.Run(async () => await StartListenerAsync(cancellationToken), cancellationToken);
        }

        Task watchdogTask = Task.CompletedTask;
        if (_enableWatchdog)
            watchdogTask = Task.Run(async () => await WatchdogAsync(cancellationToken), cancellationToken);

        await Task.WhenAll(listenerTask, watchdogTask);
    }

    private async Task StartListenerAsync(CancellationToken cancellationToken)
    {
        if (_onListenerDataReceived is null || _listener is null)
            throw new ArgumentException("expected listener data receive to start handler");

        _listener.Start();
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient? client;
            try
            {
                // client = await _listener.AcceptTcpClientAsync(cancellationToken);
                client = await _listener.AcceptTcpClientAsync();
                client.NoDelay = true; // Low latency
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Accept error: {ex.Message}");
                continue;
            }

            if (client == null)
                continue;

            var connectionId = Guid.NewGuid();
            _listenerConnections[connectionId] = client;
            _lastActivity[connectionId] = DateTime.UtcNow;

            var forward = new ForwardModelV4
            {
                ConnectionId = connectionId,
                Accessor = this
            };

            _ = Task.Run(async () =>
            {
                try
                {
                    if (_onClientConnected != null)
                        await _onClientConnected.Invoke(forward, cancellationToken);

                    var stream = client.GetStream();
                    await HandleStreamAsync(connectionId, stream, _onListenerDataReceived, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Ignored
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Listener handler error: {ex.Message}");
                }
                finally
                {
                    await CloseConnectionAsync(connectionId, cancellationToken);
                }
            }, cancellationToken);
        }
    }

    private (bool found, NetworkStream? stream) GetServerStream(Guid connectionId)
    {
        var connectionFound = _serverConnections.TryGetValue(connectionId, out var server);
        return (connectionFound, server?.GetStream());
    }

    private (bool found, NetworkStream? stream) GetListenerStream(Guid connectionId)
    {
        var connectionFound = _listenerConnections.TryGetValue(connectionId, out var listener);
        return (connectionFound, listener?.GetStream());
    }

    private async Task CloseConnectionAsync(Guid connectionId, CancellationToken ct)
    {
        if (_onClientDisconnected is null)
            return;

        var model = new ForwardModelV4 { ConnectionId = connectionId, Accessor = this };
        await _onClientDisconnected.Invoke(model, ct);
    }

    public async Task HandleStreamAsync(Guid connectionId, NetworkStream stream, Func<ForwardModelV4, CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(65536);

            try
            {
                var length = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                    .ConfigureAwait(false);

                if (length == 0)
                    break;

                _lastActivity[connectionId] = DateTime.UtcNow;

                var model = new ForwardModelV4
                {
                    ConnectionId = connectionId,
                    Accessor = this,
                    // Data = buffer.AsSpan(0, length).ToArray(),
                    Data = new  byte[length],
                    Length = length
                };

                Array.Copy(buffer ,  0, model.Data, 0, length);
                
                try
                {
                    await handler.Invoke(model, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Handler error: {ex.Message}");
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                // Stream/socket closed while waiting
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Read error: {ex.Message}");
                break;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }
        }
    }

    private async Task WatchdogAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(CheckInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var now = DateTime.UtcNow;
            foreach (var kv in _lastActivity.ToArray())
            {
                if (now - kv.Value > IdleTimeout)
                {
                    await CloseConnectionAsync(kv.Key, cancellationToken);
                }
            }
        }
    }


    /// <summary>
    /// Example Usage of class
    /// </summary>
    public static void RunExample()
    {
        var cs = new CancellationTokenSource();

        async Task OnListenerDataReceived(ForwardModelV4 context, CancellationToken ct)
        {
            await context.Accessor.SendDataToServerAsync(context.ConnectionId, context.Data, context.Length, ct);
        }

        async Task OnServerDataReceived(ForwardModelV4 context, CancellationToken ct)
        {
            await context.Accessor.SendDataToListenerAsync(context.ConnectionId, context.Data, context.Length, ct);
        }

        async Task OnNewConnection(ForwardModelV4 context, CancellationToken ct)
        {
            await context.Accessor.OpenServerConnectionAsync(context.ConnectionId, ct);
        }

        async Task OnClientDisconnected(ForwardModelV4 context, CancellationToken ct)
        {
            await context.Accessor.DisposeClientAsync(context.ConnectionId);
        }

        var config = new TcpFw4Config
        {
            Name = "sample-one",
            
            OnListenerDataReceived = OnListenerDataReceived,
            OnServerDataReceived = OnServerDataReceived,
            OnClientConnected = OnNewConnection,
            OnClientDisconnected = OnClientDisconnected,
            ListenPort = 40_000,
            TargetPort = 5900,
            
            // listenPort = 12346,
            // targetPort = 12345,
        };

        var forwarder = new TcpFwV4(config);


        forwarder.RunAsync(cs.Token).Wait(cs.Token);
    }
}

public delegate Task PacketHandler(ForwardModelV4 accessor, CancellationToken cancellationToken);

public delegate Task ConnectionHandler(ForwardModelV4 accessor, CancellationToken cancellationToken);