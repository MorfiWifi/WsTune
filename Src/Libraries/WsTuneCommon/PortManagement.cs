using System.Net;
using System.Net.Sockets;

#if NET48
using WsTuneCommon.Extensions;
#endif

namespace WsTuneCommon;

public static class PortManagement
{
    public static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
    
    
    public static async Task WaitUntilPortOpen(int port, CancellationToken token)
    {
        var start = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(5);
        while ((DateTime.UtcNow - start) < timeout)
        {
            try
            {
                using var client = new TcpClient();
                // await client.ConnectAsync(IPAddress.Loopback, port, token);
                await client.ConnectAsync(IPAddress.Loopback, port);
                return;
            }
            catch
            {
                await Task.Delay(100, token); // retry quickly
            }
        }

        throw new TimeoutException($"Port {port} did not open in time.");
    }
}