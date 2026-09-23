using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Widget;

namespace WsTune.Droid;

/// <summary>
/// Polished single-screen UI for the WsTune Listener:
/// card-based layout, structured tunnel editor (no raw JSON), named profiles
/// for the Host SignalR base address, one-time identity setup, persisted state.
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
    private AppState _state = AppState.CreateDefault();

    private EditText? _urlField;
    private EditText? _identityField;
    private TextView? _identityLockNote;
    private Spinner? _profileSpinner;
    private ArrayAdapter<string>? _profileAdapter;
    private bool _profileSpinnerSilent;

    private LinearLayout? _tunnelList;
    private Button? _toggleButton;
    private TextView? _statusText;
    private TextView? _logText;
    private ScrollView? _logScroll;

    private int Dp(int value) => (int)(value * (Resources?.DisplayMetrics?.Density ?? 2f));

    /// <summary>Resolve a color resource to <see cref="Android.Graphics.Color"/> (GetColor returns int).</summary>
    private Android.Graphics.Color ColorRes(int resourceId)
        => new(GetColor(resourceId));

    // ------------------------------------------------------------------ UI --

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        _state = AppState.Load(this);

        var page = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
        };
        page.SetBackgroundColor(ColorRes(Resource.Color.colorBackground));
        page.SetPadding(Dp(16), Dp(0), Dp(16), Dp(24));

        page.AddView(BuildHeader());

        var scroll = new ScrollView(this) { LayoutParameters = new ViewGroup.LayoutParams(-1, -1) };
        var content = new LinearLayout(this) { Orientation = Orientation.Vertical };
        content.SetPadding(0, Dp(8), 0, 0);

        content.AddView(BuildConnectionCard());
        content.AddView(BuildTunnelsCard());
        content.AddView(BuildControlCard());
        content.AddView(BuildLogCard());

        scroll.AddView(content);
        page.AddView(scroll);

        SetContentView(page);

        RenderProfiles();
        RenderTunnels();
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
        SaveState();
    }

    protected override void OnDestroy()
    {
        SaveState();
        base.OnDestroy();
    }

    // -------------------------------------------------------------- header --

    private View BuildHeader()
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetPadding(Dp(4), Dp(24), Dp(4), Dp(8));
        row.SetGravity(GravityFlags.CenterVertical);

        var icon = new ImageView(this) { LayoutParameters = new ViewGroup.LayoutParams(Dp(36), Dp(36)) };
        icon.SetImageResource(Resource.Drawable.ic_launcher_foreground);
        icon.SetBackgroundColor(ColorRes(Resource.Color.ic_launcher_background));
        icon.SetPadding(Dp(6), Dp(6), Dp(6), Dp(6));
        row.AddView(icon);

        var titles = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f),
        };
        titles.SetPadding(Dp(12), 0, 0, 0);

        var title = new TextView(this)
        {
            Text = "WsTune Listener",
            TextSize = 22,
        };
        title.SetTextColor(ColorRes(Resource.Color.colorTextPrimary));
        title.SetTypeface(null, Android.Graphics.TypefaceStyle.Bold);
        titles.AddView(title);

        var subtitle = new TextView(this) { Text = "TCP tunnels over WebSockets", TextSize = 13 };
        subtitle.SetTextColor(ColorRes(Resource.Color.colorTextSecondary));
        titles.AddView(subtitle);

        row.AddView(titles);
        return row;
    }

    // ---------------------------------------------------------- connection --

    private View BuildConnectionCard()
    {
        var card = NewCard("Connection");

        var urlLabel = FieldLabel("Host SignalR URL");
        card.AddView(urlLabel);

        _urlField = NewField(_state.SignalR, InputTypes.ClassText | InputTypes.TextVariationUri);
        _urlField!.Hint = "https://your-host/sample-endpoint";
        _urlField.TextChanged += (_, _) => { _state.SignalR = _urlField.Text?.Trim() ?? ""; SaveState(); };
        card.AddView(_urlField);

        // ---- profiles (save / load base address) ----
        var profileLabel = FieldLabel("Profiles (saved base addresses)");
        profileLabel.SetPadding(0, Dp(14), 0, Dp(4));
        card.AddView(profileLabel);

        var profileRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        profileRow.SetGravity(GravityFlags.CenterVertical);

        _profileSpinner = new Spinner(this)
        {
            LayoutParameters = new LinearLayout.LayoutParams(0, Dp(48), 1f),
            Prompt = "Select profile",
        };
        _profileAdapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem);
        _profileAdapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
        _profileSpinner.Adapter = _profileAdapter;
        _profileSpinner.ItemSelected += OnProfileSelected;
        profileRow.AddView(_profileSpinner);

        var saveBtn = NewSmallButton("Save");
        saveBtn.Click += (_, _) => PromptSaveProfile();
        profileRow.AddView(saveBtn);

        var deleteBtn = NewSmallButton("Delete");
        deleteBtn.Click += (_, _) => DeleteActiveProfile();
        profileRow.AddView(deleteBtn);

        card.AddView(profileRow);

        // ---- identity: editable only once ----
        var identityLabel = FieldLabel("Identity (set once)");
        identityLabel.SetPadding(0, Dp(14), 0, Dp(4));
        card.AddView(identityLabel);

        _identityField = NewField(_state.Identity, InputTypes.ClassText);
        _identityField!.Hint = "e.g. phone-1";
        _identityField.Enabled = !_state.IdentityLocked;
        _identityField.TextChanged += (_, _) =>
        {
            if (!_state.IdentityLocked)
            {
                _state.Identity = _identityField.Text?.Trim() ?? "";
                SaveState();
            }
        };
        card.AddView(_identityField);

        _identityLockNote = new TextView(this)
        {
            Text = _state.IdentityLocked
                ? "Locked — identity is set for this installation."
                : "Editable until the first Start, then locked.",
            TextSize = 12,
        };
        _identityLockNote.SetTextColor(ColorRes(Resource.Color.colorTextSecondary));
        _identityLockNote.SetPadding(0, Dp(6), 0, 0);
        card.AddView(_identityLockNote);

        return card;
    }

    // ------------------------------------------------------------- tunnels --

    private View BuildTunnelsCard()
    {
        var card = NewCard("Tunnels");

        var hint = new TextView(this)
        {
            Text = "Each entry: local listen port → destination via the Host.",
            TextSize = 12,
        };
        hint.SetTextColor(ColorRes(Resource.Color.colorTextSecondary));
        hint.SetPadding(0, 0, 0, Dp(8));
        card.AddView(hint);

        _tunnelList = new LinearLayout(this) { Orientation = Orientation.Vertical };
        card.AddView(_tunnelList);

        var addBtn = NewSmallButton("+ Add tunnel");
        addBtn.SetBackgroundColor(ColorRes(Resource.Color.colorPrimary));
        addBtn.SetTextColor(Android.Graphics.Color.White);
        var addLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, Dp(44)) { TopMargin = Dp(8) };
        addBtn.LayoutParameters = addLp;
        addBtn.Click += (_, _) =>
        {
            _state.Tunnels.Add(new WsTuneCommon.Models.TunnelConfigDto
            {
                Name = $"Tunnel-{_state.Tunnels.Count + 1}",
                Protocol = "TCP",
                ListenPort = 40000 + _state.Tunnels.Count,
                TargetHost = "127.0.0.1",
                TargetPort = 5900,
                Destination = "A17",
            });
            SaveState();
            RenderTunnels();
        };
        card.AddView(addBtn);

        return card;
    }

    /// <summary>Rebuild tunnel editors from the model (add/remove only — typing edits the model in place).</summary>
    private void RenderTunnels()
    {
        if (_tunnelList is null) return;
        _tunnelList.RemoveAllViews();

        if (_state.Tunnels.Count == 0)
        {
            var empty = new TextView(this) { Text = "No tunnels yet — tap “+ Add tunnel”.", TextSize = 13 };
            empty.SetTextColor(ColorRes(Resource.Color.colorTextSecondary));
            empty.SetPadding(0, Dp(4), 0, Dp(4));
            _tunnelList.AddView(empty);
            return;
        }

        for (var i = 0; i < _state.Tunnels.Count; i++)
        {
            var tunnel = _state.Tunnels[i];
            var index = i;
            var box = new LinearLayout(this)
            {
                Orientation = Orientation.Vertical,
                Background = GetDrawable(Resource.Drawable.tunnel_card_bg),
            };
            box.SetPadding(Dp(12), Dp(10), Dp(12), Dp(12));
            var boxLp = new LinearLayout.LayoutParams(-1, ViewGroup.LayoutParams.WrapContent) { TopMargin = Dp(8) };
            box.LayoutParameters = boxLp;

            // header: name preview + remove
            var head = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            head.SetGravity(GravityFlags.CenterVertical);
            var headName = new TextView(this) { TextSize = 15, LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) };
            headName.SetTextColor(Android.Graphics.Color.White);
            headName.SetTypeface(null, Android.Graphics.TypefaceStyle.Bold);
            headName.Text = string.IsNullOrWhiteSpace(tunnel.Name) ? $"Tunnel {index + 1}" : tunnel.Name;
            head.AddView(headName);

            var remove = new TextView(this) { Text = "Remove", TextSize = 13 };
            remove.SetTextColor(ColorRes(Resource.Color.colorError));
            remove.SetPadding(Dp(8), Dp(4), Dp(4), Dp(4));
            var rIdx = index;
            remove.Click += (_, _) =>
            {
                if (rIdx < _state.Tunnels.Count)
                {
                    _state.Tunnels.RemoveAt(rIdx);
                    SaveState();
                    RenderTunnels();
                }
            };
            head.AddView(remove);
            box.AddView(head);

            // Name / Destination row
            var row1 = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            row1.AddView(SpinField("Name", tunnel, t => t.Name, dark: true, weight: 1f, singleLine: true));
            row1.AddView(SpinField("Destination", tunnel, t => t.Destination, dark: true, weight: 1f, singleLine: true));
            box.AddView(row1);

            // Listen port / target port row
            var row2 = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            row2.AddView(SpinField("Listen port", tunnel, t => t.ListenPort.ToString(), dark: true, weight: 1f,
                inputTypes: InputTypes.ClassNumber, onSet: (t, v) =>
                {
                    if (int.TryParse(v, out var p)) t.ListenPort = p;
                }));
            row2.AddView(SpinField("Target port", tunnel, t => t.TargetPort.ToString(), dark: true, weight: 1f,
                inputTypes: InputTypes.ClassNumber, onSet: (t, v) =>
                {
                    if (int.TryParse(v, out var p)) t.TargetPort = p;
                }));
            box.AddView(row2);

            // Target host + protocol row
            var row3 = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            row3.AddView(SpinField("Target host", tunnel, t => t.TargetHost, dark: true, weight: 1f, singleLine: true));

            var protoCol = new LinearLayout(this)
            {
                Orientation = Orientation.Vertical,
                LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f),
            };
            protoCol.SetPadding(Dp(8), 0, 0, 0);
            var protoLabel = FieldLabel("Protocol", dark: true);
            protoCol.AddView(protoLabel);
            var protoSpin = new Spinner(this)
            {
                LayoutParameters = new LinearLayout.LayoutParams(-1, Dp(44)),
            };
            var protoAdapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem,
                new[] { "TCP", "UDP" });
            protoAdapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
            protoSpin.Adapter = protoAdapter;
            protoSpin.SetSelection(tunnel.Protocol == "UDP" ? 1 : 0);
            protoSpin.ItemSelected += (_, args) =>
            {
                tunnel.Protocol = args.Position == 1 ? "UDP" : "TCP";
                SaveState();
            };
            protoCol.AddView(protoSpin);
            row3.AddView(protoCol);

            box.AddView(row3);
            _tunnelList.AddView(box);
        }
    }

    /// <summary>Label + EditText bound to a tunnel property; edits update the model and persist.</summary>
    private View SpinField(
        string label,
        WsTuneCommon.Models.TunnelConfigDto tunnel,
        Func<WsTuneCommon.Models.TunnelConfigDto, string> get,
        bool dark,
        float weight,
        bool singleLine = false,
        InputTypes inputTypes = InputTypes.ClassText,
        Action<WsTuneCommon.Models.TunnelConfigDto, string>? onSet = null)
    {
        var col = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, weight),
        };
        col.AddView(FieldLabel(label, dark));

        var edit = new EditText(this)
        {
            Text = get(tunnel),
            LayoutParameters = new LinearLayout.LayoutParams(-1, Dp(44)),
            InputType = inputTypes,
        };
        edit.SetTextColor(dark ? Android.Graphics.Color.White : ColorRes(Resource.Color.colorTextPrimary));
        edit.SetHintTextColor(dark ? Android.Graphics.Color.Argb(120, 255, 255, 255) : ColorRes(Resource.Color.colorTextSecondary));
        edit.SetBackgroundResource(Resource.Drawable.edit_bg_selector);
        edit.SetPadding(Dp(10), 0, Dp(10), 0);
        if (singleLine || inputTypes == InputTypes.ClassNumber)
            edit.SetSingleLine(true);

        edit.TextChanged += (_, _) =>
        {
            var v = edit.Text ?? "";
            if (onSet is not null)
                onSet(tunnel, v);
            else if (label == "Name")
                tunnel.Name = v;
            else if (label == "Destination")
                tunnel.Destination = v;
            else if (label == "Target host")
                tunnel.TargetHost = v;
            SaveState();
        };

        col.AddView(edit);
        col.SetPadding(0, 0, Dp(8), Dp(4));
        return col;
    }

    // -------------------------------------------------------------- control --

    private View BuildControlCard()
    {
        var card = NewCard("Control");

        _statusText = new TextView(this) { Text = "Status: Stopped", TextSize = 14 };
        _statusText.SetTextColor(ColorRes(Resource.Color.colorTextSecondary));
        _statusText.SetPadding(0, 0, 0, Dp(10));
        card.AddView(_statusText);

        _toggleButton = new Button(this)
        {
            Text = GetString(Resource.String.btn_start),
            LayoutParameters = new LinearLayout.LayoutParams(-1, Dp(52)),
        };
        _toggleButton.SetBackgroundResource(Resource.Drawable.button_primary_bg);
        _toggleButton.SetTextColor(Android.Graphics.Color.White);
        _toggleButton.SetTextSize(Android.Util.ComplexUnitType.Sp, 16);
        _toggleButton.Click += OnToggleClicked;
        card.AddView(_toggleButton);

        return card;
    }

    private View BuildLogCard()
    {
        var card = NewCard("Log");

        _logText = new TextView(this)
        {
            TextSize = 12,
            Typeface = Android.Graphics.Typeface.Monospace,
            Text = "",
        };
        _logText.SetTextColor(ColorRes(Resource.Color.colorTextPrimary));

        _logScroll = new ScrollView(this)
        {
            LayoutParameters = new LinearLayout.LayoutParams(-1, Dp(220)),
        };
        _logScroll.AddView(_logText);
        card.AddView(_logScroll);
        return card;
    }

    // ------------------------------------------------------------- helpers --

    private LinearLayout NewCard(string title)
    {
        var outer = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new LinearLayout.LayoutParams(-1, ViewGroup.LayoutParams.WrapContent) { TopMargin = Dp(12) },
            Background = GetDrawable(Resource.Drawable.card_bg),
            Elevation = Dp(2),
        };
        outer.SetPadding(Dp(16), Dp(14), Dp(16), Dp(16));

        var head = new TextView(this) { Text = title, TextSize = 17 };
        head.SetTextColor(ColorRes(Resource.Color.colorTextPrimary));
        head.SetTypeface(null, Android.Graphics.TypefaceStyle.Bold);
        head.SetPadding(0, 0, 0, Dp(6));
        outer.AddView(head);
        return outer;
    }

    private TextView FieldLabel(string text, bool dark = false)
    {
        var label = new TextView(this) { Text = text, TextSize = 12 };
        label.SetTextColor(dark
            ? Android.Graphics.Color.Argb(180, 255, 255, 255)
            : ColorRes(Resource.Color.colorTextSecondary));
        label.SetPadding(0, 0, 0, Dp(2));
        return label;
    }

    private EditText NewField(string value, InputTypes inputType)
    {
        var edit = new EditText(this)
        {
            Text = value,
            LayoutParameters = new LinearLayout.LayoutParams(-1, Dp(48)),
            InputType = inputType,
        };
        edit.SetTextColor(ColorRes(Resource.Color.colorTextPrimary));
        edit.SetHintTextColor(ColorRes(Resource.Color.colorTextSecondary));
        edit.SetBackgroundResource(Resource.Drawable.edit_bg_selector);
        edit.SetPadding(Dp(12), 0, Dp(12), 0);
        edit.SetSingleLine(true);
        return edit;
    }

    private Button NewSmallButton(string text)
    {
        var b = new Button(this)
        {
            Text = text,
            LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, Dp(44)) { LeftMargin = Dp(8) },
        };
        b.SetBackgroundResource(Resource.Drawable.button_primary_bg);
        b.SetTextColor(Android.Graphics.Color.White);
        b.SetTextSize(Android.Util.ComplexUnitType.Sp, 13);
        return b;
    }

    // ------------------------------------------------------------ profiles --

    private void RenderProfiles()
    {
        if (_profileAdapter is null || _profileSpinner is null) return;
        _profileSpinnerSilent = true;
        _profileAdapter.Clear();
        _profileAdapter.Add("(no profile)");
        var selected = 0;
        for (var i = 0; i < _state.Profiles.Count; i++)
        {
            _profileAdapter.Add(_state.Profiles[i].Name);
            if (_state.ActiveProfile == _state.Profiles[i].Name)
                selected = i + 1;
        }
        _profileAdapter.NotifyDataSetChanged();
        _profileSpinner.SetSelection(selected);
        _profileSpinnerSilent = false;
    }

    private void OnProfileSelected(object? sender, AdapterView.ItemSelectedEventArgs e)
    {
        if (_profileSpinnerSilent || e.Position <= 0 || e.Position > _state.Profiles.Count)
            return;
        var profile = _state.Profiles[e.Position - 1];
        _state.SignalR = profile.SignalR;
        _state.ActiveProfile = profile.Name;
        if (_urlField is not null)
            _urlField.Text = profile.SignalR;
        SaveState();
        Toast.MakeText(this, $"Loaded profile “{profile.Name}”", ToastLength.Short)?.Show();
    }

    private void PromptSaveProfile()
    {
        var input = new EditText(this)
        {
            Text = _state.ActiveProfile ?? "",
            Hint = "Profile name",
            InputType = InputTypes.ClassText,
        };
        input.SetSingleLine(true);

        var padding = Dp(20);
        var wrapper = new LinearLayout(this) { Orientation = Orientation.Vertical };
        wrapper.SetPadding(padding, padding / 2, padding, 0);
        wrapper.AddView(input);

        new AlertDialog.Builder(this)
            .SetTitle("Save profile")
            .SetMessage("Stores the current Host SignalR URL under this name.")
            .SetView(wrapper)
            .SetPositiveButton("Save", (_, _) =>
            {
                var name = input.Text?.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    Toast.MakeText(this, "Name required", ToastLength.Short)?.Show();
                    return;
                }
                var url = _urlField?.Text?.Trim() ?? "";
                var existing = _state.Profiles.FirstOrDefault(p =>
                    string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                {
                    existing.SignalR = url;
                }
                else
                {
                    _state.Profiles.Add(new ProfileEntry { Name = name!, SignalR = url });
                }
                _state.ActiveProfile = name;
                SaveState();
                RenderProfiles();
                Toast.MakeText(this, $"Saved “{name}”", ToastLength.Short)?.Show();
            })
            .SetNegativeButton("Cancel", (_, _) => { })
            .Show();
    }

    private void DeleteActiveProfile()
    {
        if (_state.ActiveProfile is null)
        {
            Toast.MakeText(this, "No active profile", ToastLength.Short)?.Show();
            return;
        }
        var name = _state.ActiveProfile;
        new AlertDialog.Builder(this)
            .SetTitle("Delete profile")
            .SetMessage($"Delete “{name}”? The URL field keeps its current value.")
            .SetPositiveButton("Delete", (_, _) =>
            {
                _state.Profiles.RemoveAll(p => p.Name == name);
                _state.ActiveProfile = null;
                SaveState();
                RenderProfiles();
            })
            .SetNegativeButton("Cancel", (_, _) => { })
            .Show();
    }

    // ---------------------------------------------------------- start/stop --

    private void OnToggleClicked(object? sender, EventArgs e)
    {
        if (ListenerService.IsRunning)
        {
            var stop = new Intent(this, typeof(ListenerService)).SetAction(ListenerService.ActionStop);
            StartService(stop);
            _toggleButton?.PostDelayed(RefreshState, 800);
            return;
        }

        var signalR = _state.SignalR;
        var identity = string.IsNullOrWhiteSpace(_state.Identity) ? "android" : _state.Identity;

        if (string.IsNullOrWhiteSpace(signalR) || !signalR.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            Toast.MakeText(this, "Enter a full SignalR URL, e.g. https://your-host/sample-endpoint", ToastLength.Long)?.Show();
            return;
        }
        if (!ValidateTunnels(out var error))
        {
            Toast.MakeText(this, error, ToastLength.Long)?.Show();
            return;
        }

        // One-time identity setup: lock the field on first successful start.
        if (!_state.IdentityLocked)
        {
            _state.IdentityLocked = true;
            if (_identityField is not null) _identityField.Enabled = false;
            if (_identityLockNote is not null)
                _identityLockNote.Text = "Locked — identity is set for this installation.";
        }
        SaveState();

        var configs = System.Text.Json.JsonSerializer.Serialize(
            _state.Tunnels, WsDroidJsonContext.Default.ListTunnelConfigDto);

        var start = new Intent(this, typeof(ListenerService)).SetAction(ListenerService.ActionStart);
        start.PutExtra(ListenerService.ExtraSignalR, signalR);
        start.PutExtra(ListenerService.ExtraIdentity, identity);
        start.PutExtra(ListenerService.ExtraConfigsJson, configs);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            StartForegroundService(start);
        else
            StartService(start);

        _toggleButton?.PostDelayed(RefreshState, 800);
    }

    private bool ValidateTunnels(out string error)
    {
        if (_state.Tunnels.Count == 0)
        {
            error = "Add at least one tunnel.";
            return false;
        }
        foreach (var t in _state.Tunnels)
        {
            if (string.IsNullOrWhiteSpace(t.Name)) { error = "Every tunnel needs a Name."; return false; }
            if (t.ListenPort is < 1024 or > 65535)
            {
                error = $"“{t.Name}”: Listen port must be 1024–65535 (Android blocks lower).";
                return false;
            }
            if (t.TargetPort is < 1 or > 65535) { error = $"“{t.Name}”: Target port must be 1–65535."; return false; }
            if (string.IsNullOrWhiteSpace(t.TargetHost)) { error = $"“{t.Name}”: Target host is required."; return false; }
            if (string.IsNullOrWhiteSpace(t.Destination)) { error = $"“{t.Name}”: Destination identity is required."; return false; }
        }
        error = string.Empty;
        return true;
    }

    // ------------------------------------------------------------ feedback --

    private void SaveState()
    {
        try { _state.Save(this); } catch { /* persistence must never crash the UI */ }
    }

    private void RefreshState()
    {
        RunOnUiThread(() =>
        {
            if (_toggleButton is not null)
            {
                _toggleButton.Text = ListenerService.IsRunning
                    ? GetString(Resource.String.btn_stop)
                    : GetString(Resource.String.btn_start);
                _toggleButton.SetBackgroundColor(ListenerService.IsRunning
                    ? ColorRes(Resource.Color.colorError)
                    : ColorRes(Resource.Color.colorPrimary));
            }
            if (_statusText is not null)
            {
                _statusText.Text = "Status: " + ListenerService.Status;
                _statusText.SetTextColor(ListenerService.IsRunning
                    ? ColorRes(Resource.Color.colorSuccess)
                    : ColorRes(Resource.Color.colorTextSecondary));
            }
        });
    }

    private void AppendLog(string line)
    {
        RunOnUiThread(() =>
        {
            if (_logText is null) return;
            var current = _logText.Text ?? string.Empty;
            var lines = (current + "\n" + line).Split('\n');
            if (lines.Length > 200)
                lines = lines[^200..];
            _logText.Text = string.Join("\n", lines);
            _logScroll?.Post(() => _logScroll.FullScroll(FocusSearchDirection.Down));
            RefreshState();
        });
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
