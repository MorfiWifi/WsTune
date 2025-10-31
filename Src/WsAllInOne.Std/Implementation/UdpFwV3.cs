using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

// using WsTuneCommon.Interfaces;
// using WsTuneCommon.Models;

// namespace WsTuneCommon.Implementation;

public class UdpFwV3 : IFwV3
{
    #region Ctor

    private readonly int _listenPort;
    private readonly string _targetHost;
    private readonly int _targetPort;

    private readonly UdpClient? _listener;
    private readonly UdpClient? _server;

    private readonly Func<ForwardModel, Task>? _onListenerDataReceived;
    private readonly Func<ForwardModel, Task>? _onServerDataReceived;
    private readonly IPEndPoint _targetEndPoint;
    private IPEndPoint? _remoteEndPoint;


    public UdpFwV3(
        Func<ForwardModel, Task>? onListenerDataReceived = null,
        Func<ForwardModel, Task>? onServerDataReceived = null,
        int listenPort = 6000, string targetHost = "127.0.0.1", int targetPort = 5900)
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
        {
            _listener = new UdpClient(_listenPort);
            _listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, 1024 * 1024);  // 1MB
        }
            
        if (onServerDataReceived is not null)
            _server = new UdpClient(0);

        _targetEndPoint = new IPEndPoint(IPAddress.Parse(_targetHost), _targetPort);
    }

    #endregion


    public async Task SendDataToListener(byte[] data, int length)
    {
        if (_remoteEndPoint is not null)
        {
            await _listener.SendAsync(data, length, _remoteEndPoint);
        }
    }


    public async Task SendDataToServer(byte[] data, int length)
    {
        await _server.SendAsync(data, length, _targetEndPoint);
    }


    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"UDP forwarder running: {_listenPort} -> {_targetHost}:{_targetPort}");

        Task listenerTask = Task.CompletedTask;
        if (_onListenerDataReceived != null)
        {
            listenerTask = Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        // if (_listener.Available <= 0) continue;


                        var received = await _listener.ReceiveAsync();
                        // var receiverId = Guid.NewGuid();
                        _remoteEndPoint = received.RemoteEndPoint;

                        //invoice data received
                        var model = new ForwardModel
                        {
                            Accessor = this,
                            Data = received.Buffer,
                            Length = received.Buffer.Length
                        };

                        await _onListenerDataReceived.Invoke(model);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Listener error: {ex.Message}");
                    }
                }
            }, cancellationToken);
        }

        Task serverTask = Task.CompletedTask;
        if (_onServerDataReceived != null)
        {
            serverTask = Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        // if (_server.Available <= 0) continue;

                        var received = await _server.ReceiveAsync();

                        var model = new ForwardModel
                        {
                            Accessor = this,
                            Data = received.Buffer,
                            Length = received.Buffer.Length
                        };

                        await _onServerDataReceived.Invoke(model);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Server error: {ex.Message}");
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
        UdpFwV3? server = null;
        UdpFwV3? listener = null;
        
        Func<ForwardModel, Task> onListenerDataReceived = async (context) =>
        {
            await server.SendDataToServer(context.Data, context.Length);
        };

        Func<ForwardModel, Task> onServerDataReceived = async (context) =>
        {
            await listener.SendDataToListener(context.Data, context.Length);
        };


        server = new UdpFwV3(null, onServerDataReceived , targetHost:"192.168.110.111");
        listener = new UdpFwV3(onListenerDataReceived, null );


        var cs = new CancellationTokenSource(50_000);
        var serverTask = server.RunAsync(cs.Token);
        var clientTask = listener.RunAsync(cs.Token);
        Task.WaitAll(serverTask, clientTask);
    }
}