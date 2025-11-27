using MessagePack;
using WsTuneCommon;
using WsTuneCommon.Models;


//default Behave is web app

namespace WsTuneCli.Host;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        AppSettings appSettings = RegisterServices(builder);

        var app = builder.Build();

        app = SetAppUsings(app, appSettings);

        await app.RunAsync();
    }

    public static WebApplication SetAppUsings(WebApplication app, AppSettings appSettings)
    {
        app.MapHub<THub>(appSettings.SignalREndpoint);

        // Serve static files from wwwroot (default)
        app.UseDefaultFiles(); // Looks for index.html by default
        app.UseStaticFiles();

        return app;
    }

    public static AppSettings RegisterServices(WebApplicationBuilder builder)
    {
        builder.Services.AddSignalR(options =>
            {
                options.EnableDetailedErrors = true;
                options.MaximumReceiveMessageSize = 100 * 1024 * 1024; // 100MB, Increased from 64KB to handle larger files
                options.KeepAliveInterval = TimeSpan.FromSeconds(15); // Send keep-alive every 15 seconds
                options.ClientTimeoutInterval = TimeSpan.FromSeconds(60); // Client timeout increased to 60 seconds
                options.MaximumParallelInvocationsPerClient = 10;
            })
            .AddMessagePackProtocol(options =>
            {
                // Optional: match the same compression settings as server
                options.SerializerOptions = MessagePackSerializerOptions.Standard
                    .WithCompression(MessagePackCompression.Lz4BlockArray);
            })
            ;

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, WsAllInOneJsonContext.Default);
        });

        AppSettings appSettings = new();
        builder.Configuration.Bind(appSettings);
        builder.Services.AddSingleton(appSettings);
        return appSettings;
    }
}