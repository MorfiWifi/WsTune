using System.Buffers;
using System.Net;
using System.Net.Sockets;
using WsTuneCommon.Interfaces;
using WsTuneCommon.Models;

#if NET48
using WsTuneCommon.Extensions;
#endif

namespace WsTuneCommon.Implementation;

public class TcpFwV3 : IFwV3
{
    #region Ctor

    private readonly int _listenPort;
    private readonly string _targetHost;
    private readonly int _targetPort;

    private readonly TcpListener? _listener;
    private TcpClient? _client;
    private TcpClient? _server;

    private readonly Func<ForwardModel, Task>? _onListenerDataReceived;
    private readonly Func<ForwardModel, Task>? _onServerDataReceived;
    private readonly IPEndPoint _targetEndPoint;

    public TcpFwV3(
        Func<ForwardModel, Task>? onListenerDataReceived = null,
        Func<ForwardModel, Task>? onServerDataReceived = null,
        int listenPort = 40002, string targetHost = "127.0.0.1", int targetPort = 40001)
    {
        if (onListenerDataReceived is null && onServerDataReceived is null)
        {
            throw new ArgumentException("At least one of the data received handlers must be provided.");
        }

        _onListenerDataReceived = onListenerDataReceived;
        _onServerDataReceived = onServerDataReceived;
        _listenPort = listenPort;
        _targetHost = targetHost;
        _targetPort = targetPort;

        if (onListenerDataReceived is not null)
            _listener = new TcpListener(IPAddress.Any, _listenPort);

        _targetEndPoint = new IPEndPoint(IPAddress.Parse(_targetHost), _targetPort);
    }

    #endregion

    public async Task SendDataToListener( byte[] data, int length)
    {
        if (_client is not null && _client.Connected)
        {
            await _client.GetStream().WriteAsync(data, 0, length);
        }
    }

    public async Task SendDataToServer( byte[] data, int length)
    {
        if (_server is not null && _server.Connected)
        {
            await _server.GetStream().WriteAsync(data, 0, length);
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"TCP forwarder running: {_listenPort} -> {_targetHost}:{_targetPort}");

        Task listenerTask = Task.CompletedTask;
        if (_onListenerDataReceived != null)
        {
            listenerTask = Task.Run(async () =>
            {
                _listener.Start();
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        // _client = await _listener.AcceptTcpClientAsync(cancellationToken);
                        _client = await _listener.AcceptTcpClientAsync();
                        _client.NoDelay = true; // Low latency

                        if (_onServerDataReceived != null)
                        {
                            // Connect to target only after client connection (fixes problem 1)
                            _server = new TcpClient();
                            _server.NoDelay = true;
                            // await _server.ConnectAsync(_targetEndPoint.Address, _targetEndPoint.Port, cancellationToken);
                            await _server.ConnectAsync(_targetEndPoint.Address, _targetEndPoint.Port);

                            // Start server read loop in a separate task
                            var serverReadTask = Task.Run(async () =>
                            {
                                var stream = _server.GetStream();
                                while (!cancellationToken.IsCancellationRequested)
                                {
                                    byte[] buffer = ArrayPool<byte>.Shared.Rent(65536); // 64KB

                                    int length = 0;
                                    try
                                    {
                                        length = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                                        if (length == 0)
                                        {
                                            break;
                                        }

                                        // Use Span for zero-copy if handlers support it
                                        // var dataSpan = buffer.AsSpan(0, length);
                                        
                                        
                                        var model = new ForwardModel
                                        {
                                            Accessor = this,
                                            Data = new  byte[length],
                                            Length = length
                                        };
                                        
                                        Array.Copy(buffer ,  0, model.Data, 0, length);


                                        try
                                        {
                                            await _onServerDataReceived.Invoke(model);
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"Server handler error: {ex.Message}");
                                        }
                                    }
                                    catch (OperationCanceledException)
                                    {
                                        break;
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"Server read error: {ex.Message}");
                                        break;
                                    }
                                    finally
                                    {
                                        ArrayPool<byte>.Shared.Return(buffer, clearArray: true); // Clear for security
                                    }
                                }

                                // Close client on server disconnect
                                _client?.Close();
                            }, cancellationToken);

                            // Listener read loop
                            var clientStream = _client.GetStream();
                            while (!cancellationToken.IsCancellationRequested)
                            {
                                byte[] buffer = ArrayPool<byte>.Shared.Rent(65536); // 64KB

                                int length = 0;
                                try
                                {
                                    length = await clientStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);

                                    if (length == 0)
                                    {
                                        break;
                                    }

                                    // Use Span for zero-copy if handlers support it
                                    // var dataSpan = buffer.AsSpan(0, length);

                                    var model = new ForwardModel
                                    {
                                        Accessor = this,
                                        // Data = dataSpan.ToArray(),
                                        Data = new  byte[length],
                                        Length = length
                                    };
                                    
                                    Array.Copy(buffer ,  0, model.Data, 0, length);

                                    try
                                    {
                                        await _onListenerDataReceived.Invoke(model);
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"Listener handler error: {ex.Message}");
                                    }
                                }
                                catch (OperationCanceledException)
                                {
                                    break;
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Listener read error: {ex.Message}");
                                    break;
                                }
                            }

                            // Close server on client disconnect
                            _server?.Close();

                            // Wait for server read task to complete
                            await serverReadTask;
                        }
                        else
                        {
                            // If no server handler, just handle listener
                            var stream = _client.GetStream();
                            while (!cancellationToken.IsCancellationRequested)
                            {
                                byte[] buffer = ArrayPool<byte>.Shared.Rent(65536); // 64KB
                                int length = 0;
                                try
                                {
                                    length = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);


                                    if (length == 0)
                                    {
                                        break;
                                    }


                                    // var dataSpan = buffer.AsSpan(0, length);

                                    var model = new ForwardModel
                                    {
                                        Accessor = this,
                                        // Data = dataSpan.ToArray(),
                                        Data = new byte[length],
                                        Length = length
                                    };

                                    Array.Copy(buffer ,  0, model.Data, 0, length);
                                    
                                    try
                                    {
                                        await _onListenerDataReceived.Invoke(model);
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"Listener handler error: {ex.Message}");
                                    }
                                }
                                catch (OperationCanceledException)
                                {
                                    break;
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Listener read error: {ex.Message}");
                                    break;
                                }
                                finally
                                {
                                    ArrayPool<byte>.Shared.Return(buffer, clearArray: true); // Clear for security
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Accept error: {ex.Message}");
                    }
                    finally
                    {
                        _client?.Close();
                        _client = null;
                        _server?.Close();
                        _server = null;
                    }
                }
            }, cancellationToken);
        }

        Task serverTask = Task.CompletedTask;
        if (_onServerDataReceived != null && _onListenerDataReceived == null)
        {
            serverTask = Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        _server = new TcpClient();
                        // await _server.ConnectAsync(_targetEndPoint.Address, _targetEndPoint.Port, cancellationToken);
                        await _server.ConnectAsync(_targetEndPoint.Address, _targetEndPoint.Port);

                        var stream = _server.GetStream();

                        while (!cancellationToken.IsCancellationRequested)
                        {
                            byte[] buffer = ArrayPool<byte>.Shared.Rent(65536); // 64KB
                            int length = 0;
                            try
                            {
                                length = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);


                                if (length == 0)
                                {
                                    break;
                                }

                                var model = new ForwardModel
                                {
                                    Accessor = this,
                                    // Data = buffer.AsSpan(0,length).ToArray(),
                                    Data = new  byte[length],
                                    Length = length
                                };

                                Array.Copy(buffer ,  0, model.Data, 0, length);
                                
                                try
                                {
                                    await _onServerDataReceived.Invoke(model);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Server handler error: {ex.Message}");
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                break;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Server read error: {ex.Message}");
                                break;
                            }
                            finally
                            {
                                ArrayPool<byte>.Shared.Return(buffer, clearArray: true); // Clear for security
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Connect error: {ex.Message}");
                    }
                    finally
                    {
                        _server?.Close();
                        _server = null;
                    }
                }
            }, cancellationToken);
        }

        await Task.WhenAll(listenerTask, serverTask);
    }

    /// <summary>
    /// Example Usage of class
    /// </summary>
    public static void RunExample()
    {
        Func<ForwardModel, Task> onListenerDataReceived = async (context) =>
        {
            await context.Accessor.SendDataToServer(context.Data, context.Length);
        };

        Func<ForwardModel, Task> onServerDataReceived = async (context) =>
        {
            await context.Accessor.SendDataToListener(context.Data, context.Length);
        };

        var forwarder = new TcpFwV3(onListenerDataReceived, onServerDataReceived);

        var cs = new CancellationTokenSource(50_000);
        forwarder.RunAsync(cs.Token).Wait();
    }
}