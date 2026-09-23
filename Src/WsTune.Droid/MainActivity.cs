using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace WsTune.Droid;

/// <summary>
/// Single-screen UI: Host SignalR URL + Identity + tunnel list (JSON) + Start/Stop + live log.
/// Config is persisted in SharedPreferences so the tunnel survives app restarts.
/// </summary>
[Activity(
    Label = "WsTune Listener",
    MainLauncher = true,
    Exported = true,
    Theme = "@style/MainTheme",
    ConfigurationChanges = Android.Content.PM.ConfigChanges.ScreenSize
        | Android.Content.PM.ConfigChanges.Orientation
        | Android.Content.PM.ConfigChanges.KeyboardHidden)]
public sealed class MainActivity : Activity
{
    private const string Prefs = "wstune";
    private const string DefaultSignalR = "https://your-host/sample-endpoint";
    private const string DefaultIdentity = "android";
    private const string DefaultConfigs = """
        [
          {
            "Name": "Phone-VNC",
            "Protocol": "TCP",
            "ListenPort": 40000,
            "TargetHost": "127.0.0.1",
            "TargetPort": 5900,
            "Destination": "A17"
          }
        ]
        """;

    private EditText? _signalRText;
    private EditText? _identityText;
    private EditText? _configsText;
    private Button? _toggleButton;
    private TextView? _statusText;
    private TextView? _logText;
    private ScrollView? _logScroll;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var prefs = GetSharedPreferences(Prefs, FileCreationMode.Private);

        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetPadding(24, 24, 24, 24);
        int Wrap = ViewGroup.LayoutParams.WrapContent;
        int Match = ViewGroup.LayoutParams.MatchParent;

        TextView Label(string text)
        {
            return new TextView(this) { Text = text, TextSize = 14 };
        }

        EditText Input(string value, bool multi = false)
        {
            var et = new EditText(this)
            {
                Text = value,
                LayoutParameters = new LinearLayout.LayoutParams(Match, Wrap),
            };
            if (multi)
            {
                et.SetMinLines(6);
                et.Gravity = GravityFlags.Top;
                et.InputType = Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextFlagMultiLine;
            }
            else
            {
                et.SingleLine = true;
            }
            return et;
        }

        root.AddView(Label("Host SignalR URL (full https/wss URL of the Host endpoint)"));
        _signalRText = Input(prefs.GetString("signalR", DefaultSignalR) ?? DefaultSignalR);
        root.AddView(_signalRText);

        root.AddView(Label("Identity (this device name, made unique automatically)"));
        _identityText = Input(prefs.GetString("identity", DefaultIdentity) ?? DefaultIdentity);
        root.AddView(_identityText);

        root.AddView(Label("Tunnels (JSON array: Name, Protocol, ListenPort, TargetHost, TargetPort, Destination)"));
        _configsText = Input(prefs.GetString("configs", DefaultConfigs) ?? DefaultConfigs, multi: true);
        root.AddView(_configsText);

        _statusText = new TextView(this) { TextSize = 14 };
        root.AddView(_statusText);

        _toggleButton = new Button(this) { Text = "Start listener" };
        _toggleButton.Click += OnToggleClicked;
        root.AddView(_toggleButton);

        root.AddView(Label("Log"));
        _logText = new TextView(this)
        {
            TextSize = 12,
            Typeface = Android.Graphics.Typeface.Monospace,
        };
        // Fixed height: this ScrollView lives inside the outer page ScrollView,
        // so layout_weight would not resolve — use ~350dp instead.
        int logHeightPx = (int)(350 * Resources?.DisplayMetrics?.Density ?? 2);
        _logScroll = new ScrollView(this)
        {
            LayoutParameters = new LinearLayout.LayoutParams(Match, logHeightPx),
        };
        _logScroll.AddView(_logText);
        root.AddView(_logScroll);

        var scroll = new ScrollView(this);
        scroll.AddView(root);
        SetContentView(scroll);

        RefreshState();
        RequestNotificationPermissionIfNeeded();
    }

    protected override void OnResume()
    {
        base.OnResume();
        ListenerService.LogProvider.OnLog += AppendLog;
        RefreshState();
    }

    protected override void OnPause()
    {
        base.OnPause();
        ListenerService.LogProvider.OnLog -= AppendLog;
    }

    private void OnToggleClicked(object? sender, EventArgs e)
    {
        var signalR = _signalRText?.Text?.Trim() ?? string.Empty;
        var identity = _identityText?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(identity)) identity = "android";
        var configs = _configsText?.Text ?? "[]";

        if (string.IsNullOrWhiteSpace(signalR) || !signalR.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            Toast.MakeText(this, "Enter the full SignalR URL, e.g. https://your-host/sample-endpoint", ToastLength.Long)?.Show();
            return;
        }
        if (!IsValidTunnelJson(configs, out var error))
        {
            Toast.MakeText(this, $"Tunnels JSON invalid: {error}", ToastLength.Long)?.Show();
            return;
        }

        GetSharedPreferences(Prefs, FileCreationMode.Private).Edit()
            .PutString("signalR", signalR)
            .PutString("identity", identity)
            .PutString("configs", configs)
            .Apply();

        if (ListenerService.IsRunning)
        {
            var stop = new Intent(this, typeof(ListenerService)).SetAction(ListenerService.ActionStop);
            StartService(stop);
        }
        else
        {
            var start = new Intent(this, typeof(ListenerService)).SetAction(ListenerService.ActionStart);
            start.PutExtra(ListenerService.ExtraSignalR, signalR);
            start.PutExtra(ListenerService.ExtraIdentity, identity);
            start.PutExtra(ListenerService.ExtraConfigsJson, configs);
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                StartForegroundService(start);
            else
                StartService(start);
        }

        // Give the service a moment, then refresh the button label.
        _toggleButton?.PostDelayed(RefreshState, 800);
    }

    private void RefreshState()
    {
        RunOnUiThread(() =>
        {
            if (_toggleButton is not null)
                _toggleButton.Text = ListenerService.IsRunning ? "Stop listener" : "Start listener";
            if (_statusText is not null)
                _statusText.Text = "Status: " + ListenerService.Status;
        });
    }

    private void AppendLog(string line)
    {
        RunOnUiThread(() =>
        {
            if (_logText is null) return;
            var current = _logText.Text ?? string.Empty;
            // Keep the on-screen buffer bounded (~200 lines).
            var lines = (current + "\n" + line).Split('\n');
            if (lines.Length > 200)
                lines = lines[^200..];
            _logText.Text = string.Join("\n", lines);
            _logScroll?.Post(() => _logScroll.FullScroll(FocusSearchDirection.Down));
            RefreshState();
        });
    }

    private static bool IsValidTunnelJson(string json, out string error)
    {
        try
        {
            var parsed = System.Text.Json.JsonSerializer.Deserialize(
                json, WsDroidJsonContext.Default.ListTunnelConfigDto);
            if (parsed is null || parsed.Count == 0)
            {
                error = "add at least one tunnel object";
                return false;
            }
            foreach (var t in parsed)
            {
                if (string.IsNullOrWhiteSpace(t.Name)) { error = "each tunnel needs a Name"; return false; }
                if (t.ListenPort is < 1024 or > 65535) { error = $"tunnel '{t.Name}': ListenPort must be 1024-65535 (Android blocks <1024)"; return false; }
                if (string.IsNullOrWhiteSpace(t.Destination)) { error = $"tunnel '{t.Name}': Destination is required"; return false; }
            }
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private void RequestNotificationPermissionIfNeeded()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu)
            return;
        if (CheckSelfPermission(Android.Manifest.Permission.PostNotifications) == Permission.Granted)
            return;
        RequestPermissions(new[] { Android.Manifest.Permission.PostNotifications }, 1001);
    }
}
