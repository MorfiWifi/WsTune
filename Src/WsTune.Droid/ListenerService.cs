using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Microsoft.Extensions.Logging;
using WsTuneCommon.Models;

namespace WsTune.Droid;

/// <summary>
/// Foreground service that keeps the WsTune listener tunnel alive when the
/// activity is in the background. Started with the tunnel config as intent extras.
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class ListenerService : Service
{
    public const string ActionStart = "com.wstune.listener.START";
    public const string ActionStop = "com.wstune.listener.STOP";
    public const string ExtraSignalR = "signalR";
    public const string ExtraIdentity = "identity";
    public const string ExtraConfigsJson = "configsJson";

    private const string ChannelId = "wstune_listener";
    private const int NotificationId = 1001;

    private ListenerRunner? _runner;
    private ILogger? _logger;
    private ILoggerFactory? _loggerFactory;
    internal static readonly AndroidLoggerProvider LogProvider = new();

    public static bool IsRunning { get; private set; }
    public static string Status { get; private set; } = "Stopped";

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var action = intent?.Action;
        if (action == ActionStop)
        {
            _ = StopRunnerAsync();
            return StartCommandResult.NotSticky;
        }

        var signalR = intent?.GetStringExtra(ExtraSignalR) ?? string.Empty;
        var identity = intent?.GetStringExtra(ExtraIdentity) ?? "android";
        var configsJson = intent?.GetStringExtra(ExtraConfigsJson) ?? "[]";

        if (string.IsNullOrWhiteSpace(signalR))
        {
            // System restarted a sticky service with no config: nothing to do.
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        CreateNotificationChannel();
        var notification = BuildNotification("WsTune Listener starting…");
#pragma warning disable CA1416 // validated: minSdk 26, StartForeground(id, notif, type) needs API 29+
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
            StartForeground(NotificationId, notification, ForegroundService.TypeDataSync);
        else
            StartForeground(NotificationId, notification);
#pragma warning restore CA1416

        _ = StartRunnerAsync(signalR, identity, configsJson);
        return StartCommandResult.Sticky;
    }

    private async Task StartRunnerAsync(string signalR, string identity, string configsJson)
    {
        try
        {
            await StopRunnerInternalAsync();

            List<TunnelConfigDto> configs;
            try
            {
                configs = System.Text.Json.JsonSerializer.Deserialize(
                    configsJson, WsDroidJsonContext.Default.ListTunnelConfigDto)
                    ?? new List<TunnelConfigDto>();
            }
            catch (Exception ex)
            {
                UpdateNotification($"Config error: {ex.Message}");
                return;
            }

            _loggerFactory?.Dispose();
            _loggerFactory = LoggerFactory.Create(b =>
            {
                b.SetMinimumLevel(LogLevel.Debug);
                b.AddProvider(LogProvider);
            });
            _logger = _loggerFactory.CreateLogger("WsTune.Droid");

            var settings = new AppSettings
            {
                Identity = identity,
                SignalREndpoint = signalR,
                Configs = configs,
            };

            _runner = new ListenerRunner(settings, _logger);
            IsRunning = true;
            Status = $"Running as {identity} ({configs.Count} tunnel(s))";
            UpdateNotification($"Running as {identity} — {configs.Count} tunnel(s)");
            await _runner.StartAsync();
            Status = $"Running as {_runner.ResolvedIdentity}";
            UpdateNotification($"Connected as {_runner.ResolvedIdentity}");
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
            UpdateNotification($"Error: {ex.Message}");
            LogProvider.Report($"{DateTime.Now:HH:mm:ss} [Error] startup failed: {ex}");
        }
    }

    private async Task StopRunnerAsync()
    {
        await StopRunnerInternalAsync();
        try { StopForeground(true); } catch { /* ignore */ }
        StopSelf();
    }

    private async Task StopRunnerInternalAsync()
    {
        if (_runner is not null)
        {
            try { await _runner.StopAsync(); } catch { /* ignore */ }
            _runner = null;
        }
        try { _loggerFactory?.Dispose(); } catch { /* ignore */ }
        _loggerFactory = null;
        _logger = null;
        IsRunning = false;
        Status = "Stopped";
    }

    public override void OnDestroy()
    {
        _ = StopRunnerInternalAsync();
        base.OnDestroy();
    }

    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
            return;
        var manager = (NotificationManager?)GetSystemService(NotificationService);
        var channel = new NotificationChannel(ChannelId, "WsTune Listener",
            NotificationImportance.Low)
        {
            Description = "Keeps the WsTune tunnel running in the background.",
        };
        manager?.CreateNotificationChannel(channel);
    }

    private Notification BuildNotification(string text)
    {
        var intent = new Intent(this, typeof(MainActivity));
        PendingIntentFlags flags = PendingIntentFlags.UpdateCurrent;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
            flags |= PendingIntentFlags.Immutable;
        var pending = PendingIntent.GetActivity(this, 0, intent, flags);
        // Platform builder (no AndroidX dependency): minSdk is 26 so channels exist.
        return new Notification.Builder(this, ChannelId)
            .SetContentTitle("WsTune Listener")
            .SetContentText(text)
            .SetSmallIcon(Android.Resource.Drawable.StatSysUpload)
            .SetContentIntent(pending)
            .SetOngoing(true)
            .Build();
    }

    private void UpdateNotification(string text)
    {
        try
        {
            var manager = (NotificationManager?)GetSystemService(NotificationService);
            manager?.Notify(NotificationId, BuildNotification(text));
        }
        catch { /* ignore */ }
    }
}
