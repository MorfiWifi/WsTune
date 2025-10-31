using System.IO;
using System.Text.Json;
using WsTuneCli.Listener.Transport;
using WsTuneCommon.Models;

public class Program
{
    public static void Main(string[] args)
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var configJson = File.ReadAllText(configPath);
        var appSettings = JsonSerializer.Deserialize<AppSettings>(configJson);
        appSettings.SignalREndpoint = args.Length > 1 ? args[1] : appSettings.SignalREndpoint;

        var transportHost = new TransportHostService(appSettings);
        var backgroundTas = transportHost.StartAsync(CancellationToken.None);
        
        Console.WriteLine("Press any key to exit");
        Console.ReadLine();

        // Task t1 = Task.CompletedTask;
        // Task t2 = Task.CompletedTask;
        //
        // List<Task> task = new List<Task>() { t1, t2 };
        //
        // Parallel.ForEach(task , async void (task1) =>
        // {
        //     try
        //     {
        //         await task1;
        //     }
        //     catch (Exception e)
        //     {
        //         throw; // TODO handle exception
        //     }
        // });

    }
}