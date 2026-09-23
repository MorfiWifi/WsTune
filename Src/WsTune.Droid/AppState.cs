using WsTuneCommon.Models;

namespace WsTune.Droid;

/// <summary>One named base-address (Host SignalR URL) the user can save/load.</summary>
public sealed class ProfileEntry
{
    public string Name { get; set; } = string.Empty;
    public string SignalR { get; set; } = string.Empty;
}

/// <summary>
/// Full app configuration, persisted as JSON in SharedPreferences so the
/// tunnel survives app restarts and process death.
/// </summary>
public sealed class AppState
{
    public const string PrefsName = "wstune";
    public const string StateKey = "state";

    public string SignalR { get; set; } = "https://your-host/sample-endpoint";
    public string Identity { get; set; } = "android";

    /// <summary>Identity is editable only for the first setup, then locked.</summary>
    public bool IdentityLocked { get; set; }

    public List<TunnelConfigDto> Tunnels { get; set; } = [];
    public List<ProfileEntry> Profiles { get; set; } = [];
    public string? ActiveProfile { get; set; }

    public static AppState CreateDefault() => new()
    {
        Tunnels =
        [
            new TunnelConfigDto
            {
                Name = "Phone-VNC",
                Protocol = "TCP",
                ListenPort = 40000,
                TargetHost = "127.0.0.1",
                TargetPort = 5900,
                Destination = "A17",
            },
        ],
    };

    /// <summary>
    /// Load from SharedPreferences; migrates the pre-profile flat keys
    /// (signalR / identity / configs) written by the first alpha build.
    /// </summary>
    public static AppState Load(Android.Content.Context context)
    {
        var prefs = context.GetSharedPreferences(PrefsName, Android.Content.FileCreationMode.Private);
        var json = prefs.GetString(StateKey, null);
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                var state = System.Text.Json.JsonSerializer.Deserialize(json, WsDroidJsonContext.Default.AppState);
                if (state is not null)
                {
                    state.Tunnels ??= [];
                    state.Profiles ??= [];
                    return state;
                }
            }
            catch
            {
                // fall through to legacy migration
            }
        }

        // Legacy flat keys (alpha build) -----------------------------------
        var legacySignalR = prefs.GetString("signalR", null);
        var legacyIdentity = prefs.GetString("identity", null);
        var legacyConfigs = prefs.GetString("configs", null);
        if (legacySignalR is null && legacyIdentity is null && legacyConfigs is null)
            return CreateDefault();

        var migrated = CreateDefault();
        if (!string.IsNullOrWhiteSpace(legacySignalR)) migrated.SignalR = legacySignalR;
        if (!string.IsNullOrWhiteSpace(legacyIdentity)) migrated.Identity = legacyIdentity;
        if (!string.IsNullOrWhiteSpace(legacyConfigs))
        {
            try
            {
                var tunnels = System.Text.Json.JsonSerializer.Deserialize(
                    legacyConfigs, WsDroidJsonContext.Default.ListTunnelConfigDto);
                if (tunnels is { Count: > 0 }) migrated.Tunnels = tunnels;
            }
            catch { /* keep defaults */ }
        }
        return migrated;
    }

    public void Save(Android.Content.Context context)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(this, WsDroidJsonContext.Default.AppState);
        context.GetSharedPreferences(PrefsName, Android.Content.FileCreationMode.Private)
            .Edit()
            .PutString(StateKey, json)
            .Apply();
    }
}
