using WsAllInOne;
using WsAllInOne.Extensions;
using WsAllInOne.Services;
using WsTuneCommon.Models;

var mode = "host";
if (args.Length > 0)
    mode = args[0];

if (mode == "listener")
{
    await WsTuneCli.Listener.Program.Main(args);
}

if (mode == "server")
{
    await WsTuneCli.Server.Program.Main(args);
}

//random identity for internal usage
Storage.ListenerIdentity = Guid.NewGuid().ToString();

var builder = WebApplication.CreateBuilder(args);
// builder.Services.AddCors();

builder.Services.AddHostedService<InternalHubService>();

AppSettings appSettings = WsTuneCli.Host.Program.RegisterServices(builder);

var app = builder.Build();

//web socket for VNC
app.UseWebsockify(appSettings.WebSockifyEndpoint);

app = WsTuneCli.Host.Program.SetAppUsings(app, appSettings);


var maxConfigs = 10;

app.AddConfigurationEndpoints(maxConfigs);

// app.UseCors(op =>
// {
//     op.AllowAnyHeader();
//     op.AllowAnyMethod();
//     op.AllowAnyOrigin();
// });

await app.RunAsync();