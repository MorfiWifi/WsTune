using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WsTuneCli.Listener.Transport;
using WsTuneCommon.ConsoleHelpers;
using WsTuneCommon.Models;

namespace WsTuneCli.Listener;

public class Program
{
    public static async Task Main(string[] args)
    {
        ConsoleHelper.DisableQuickEditMode();  // Prevent console pause on click

        var host = Host.CreateDefaultBuilder(args)  // Pass args here
            .ConfigureAppConfiguration((hostingContext, config) =>
            {
                var env = hostingContext.HostingEnvironment;

                config
                    .SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("appsettings.listener.json", optional: true, reloadOnChange: false)
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                    .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: false)
                    .AddEnvironmentVariables()
                    .AddCommandLine(args);
            })
            .ConfigureServices((context, services) =>
            {
                var config = context.Configuration;
                
                AppSettings appSettings = new();
                config.Bind(appSettings);
                services.AddSingleton(appSettings);

                services.AddLogging(builder =>
                {
                    builder.AddConfiguration(config.GetSection("Logging"));
                    builder.AddConsole();
                });
                
                services.AddHostedService<TransportHostService>();
            })
            .Build();

        await host.RunAsync();
    }
}