using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using Google.Android.Material.AppBar;
using Google.Android.Material.Button;
using Google.Android.Material.Card;
using Google.Android.Material.Chip;
using Google.Android.Material.Dialog;
using Google.Android.Material.Snackbar;
using Google.Android.Material.TextField;

namespace WsTune.Droid;

/// <summary>
/// Material Design 3 single-screen UI for the WsTune Listener: top app bar,
/// outlined text fields, card surfaces, chip protocol selector, exposed-dropdown
/// profiles, Snackbar feedback, sticky bottom Start/Stop bar. State is persisted
/// in <see cref="AppState"/> (SharedPreferences).
/// Material widgets require AppCompatActivity (not plain Activity).
/// </summary>
[Activity(
    Label = "WsTune Listener",
    MainLauncher = true,
    Exported = true,
    Theme = "@style/MainTheme",
    ConfigurationChanges = Android.Content.PM.ConfigChanges.ScreenSize
        | Android.Content.PM.ConfigChanges.Orientation
        | Android.Content.PM.ConfigChanges.KeyboardHidden
        | Android.Content.PM.ConfigChanges.UiMode)]
public sealed class MainActivity : AppCompatActivity
{
    private AppState _state = AppState.CreateDefault();

    private LinearLayout? _root;
    private TextInputEditText? _urlEdit;
    private TextInputLayout? _identityTil;
    private TextInputEditText? _identityEdit;
    private MaterialAutoCompleteTextView? _profileDropdown;
    private bool _profileSilent;

    private LinearLayout? _tunnelList;
    private MaterialButton? _toggleButton;
    private Chip? _statusChip;
    private TextView? _logText;
    private ScrollView? _logScroll;

    private int Dp(int value) => (int)(value * (Resources?.DisplayMetrics?.Density ?? 2f));

    /// <summary>Resolve a color resource to <see cref="Android.Graphics.Color"/> (GetColor returns int).</summary>
    private Android.Graphics.Color ColorRes(int resourceId)
        => new(GetColor(resourceId));

    private sealed class ActionClickListener(Action action) : Java.Lang.Object, View.IOnClickListener
    {
        public void OnClick(View? v) => action();
    }

    private static View.IOnClickListener Click(Action action) => new ActionClickListener(action);

    // ------------------------------------------------------------------ UI --

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        try
        {
            base.OnCreate(savedInstanceState);
            BuildUi(savedInstanceState);
            RequestNotificationPermissionIfNeeded();
        }
        catch (System.Exception ex)
        {
            Android.Util.Log.Error("WsTune", "MainActivity.OnCreate failed: " + ex);
            throw;
        }
    }

    private void BuildUi(Bundle? savedInstanceState)
    {
        _state = AppState.Load(this);

        var root = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
        };
        _root = root;
        root.SetBackgroundColor(ColorRes(Resource.Color.colorBackground));
        root.AddView(BuildToolbar());

        var scroll = new ScrollView(this)
        {
            LayoutParameters = new LinearLayout.LayoutParams(-1, 0, 1f),
            VerticalScrollBarEnabled = false,
        };
        var content = new LinearLayout(this) { Orientation = Orientation.Vertical };
        content.SetPadding(Dp(16), Dp(4), Dp(16), Dp(16));
        content.AddView(BuildConnectionCard());
        content.AddView(BuildTunnelsCard());
        content.AddView(BuildLogCard());
        scroll.AddView(content);
        root.AddView(scroll);

        root.AddView(BuildBottomBar());

        SetContentView(root);

        RenderProfiles();
        RenderTunnels();
        RefreshState();
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

    // ---------------------------------------------------------- top app bar --

    private View BuildToolbar()
    {
        var toolbar = new MaterialToolbar(this)
        {
            LayoutParameters = new LinearLayout.LayoutParams(-1, ViewGroup.LayoutParams.WrapContent),
        };
        toolbar.SetPadding(Dp(16), Dp(8), Dp(16), Dp(8));
        toolbar.Title = GetString(Resource.String.app_name);
        toolbar.SetTitleTextColor(ColorRes(Resource.Color.colorOnSurface));
        toolbar.SetSubtitle(Resource.String.toolbar_subtitle);
        toolbar.SetSubtitleTextColor(ColorRes(Resource.Color.colorOnSurfaceVariant));
        return toolbar;
    }

    // ---------------------------------------------------------- connection --

    private View BuildConnectionCard()
    {
        var content = NewCard(Resource.String.card_connection);

        var urlTil = OutlinedField("Host SignalR URL", InputTypes.ClassText | InputTypes.TextVariationUri);
        urlTil.EndIconMode = TextInputLayout.EndIconClearText;
        _urlEdit = (TextInputEditText)urlTil.EditText!;
        _urlEdit.Text = _state.SignalR;
        _urlEdit.TextChanged += (_, _) => { _state.SignalR = _urlEdit.Text?.Trim() ?? ""; SaveState(); };
        content.AddView(urlTil);

        // ---- profiles (exposed dropdown + icon actions) ----
        var profileRow = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new LinearLayout.LayoutParams(-1, ViewGroup.LayoutParams.WrapContent) { TopMargin = Dp(8) },
        };
        profileRow.SetGravity(GravityFlags.CenterVertical);

        var profileTil = new TextInputLayout(this)
        {
            LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f),
            Hint = GetString(Resource.String.hint_profile),
        };
        // Exposed-dropdown look without passing a widget style as defStyleAttr.
        profileTil.EndIconMode = TextInputLayout.EndIconCustom;
        profileTil.SetEndIconDrawable(Resource.Drawable.ic_tunnel);
        profileTil.EndIconContentDescription = GetString(Resource.String.hint_profile);

        _profileDropdown = new MaterialAutoCompleteTextView(this)
        {
            LayoutParameters = new LinearLayout.LayoutParams(-1, ViewGroup.LayoutParams.WrapContent),
        };
        _profileDropdown.SetSingleLine(true);
        _profileDropdown.ItemClick += OnProfileClick;
        profileTil.AddView(_profileDropdown);
        profileRow.AddView(profileTil);

        var saveProfile = IconButton(Resource.String.cd_save_profile, Resource.Drawable.ic_save);
        saveProfile.Click += (_, _) => PromptSaveProfile();
        profileRow.AddView(saveProfile);

        var deleteProfile = IconButton(Resource.String.cd_delete_profile, Resource.Drawable.ic_delete);
        deleteProfile.Click += (_, _) => DeleteActiveProfile();
        profileRow.AddView(deleteProfile);

        content.AddView(profileRow);

        // ---- identity: editable only once ----
        var identityTil = OutlinedField(GetString(Resource.String.hint_identity), InputTypes.ClassText);
        var idLp = (LinearLayout.LayoutParams)identityTil.LayoutParameters!;
        idLp.TopMargin = Dp(8);
        identityTil.LayoutParameters = idLp;
        _identityTil = identityTil;
        _identityEdit = (TextInputEditText)identityTil.EditText!;
        _identityEdit.Text = _state.Identity;
        _identityEdit.TextChanged += (_, _) =>
        {
            if (!_state.IdentityLocked)
            {
                _state.Identity = _identityEdit.Text?.Trim() ?? "";
                SaveState();
            }
        };
        ApplyIdentityLockState();
        content.AddView(identityTil);

        return content;
    }

    private void ApplyIdentityLockState()
    {
        if (_identityTil is null) return;
        var locked = _state.IdentityLocked;
        _identityTil.Enabled = !locked;
        _identityTil.HelperTextEnabled = true;
        _identityTil.HelperText = GetString(locked
            ? Resource.String.helper_identity_locked
            : Resource.String.helper_identity_open);
        if (locked)
        {
            _identityTil.EndIconMode = TextInputLayout.EndIconCustom;
            _identityTil.SetEndIconDrawable(Resource.Drawable.ic_lock);
            _identityTil.EndIconContentDescription = GetString(Resource.String.helper_identity_locked);
        }
        else
        {
            _identityTil.EndIconMode = TextInputLayout.EndIconNone;
        }
    }

    // ------------------------------------------------------------- tunnels --

    private View BuildTunnelsCard()
    {
        var content = NewCard(Resource.String.card_tunnels);

        var hint = new TextView(this)
        {
            Text = GetString(Resource.String.tunnels_hint),
            TextSize = 12,
        };
        hint.SetTextColor(ColorRes(Resource.Color.colorOnSurfaceVariant));
        hint.SetPadding(0, 0, 0, Dp(4));
        content.AddView(hint);

        _tunnelList = new LinearLayout(this) { Orientation = Orientation.Vertical };
        content.AddView(_tunnelList);

        var addBtn = new MaterialButton(this)
        {
            Text = GetString(Resource.String.btn_add_tunnel),
            LayoutParameters = new LinearLayout.LayoutParams(-1, ViewGroup.LayoutParams.WrapContent) { TopMargin = Dp(12) },
        };
        addBtn.SetIconResource(Resource.Drawable.ic_add);
        addBtn.IconGravity = MaterialButton.IconGravityTextStart;
        addBtn.IconPadding = Dp(8);
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
        content.AddView(addBtn);

        return content;
    }

    /// <summary>Rebuild tunnel editors from the model (add/remove only — typing edits the model in place).</summary>
    private void RenderTunnels()
    {
        if (_tunnelList is null) return;
        _tunnelList.RemoveAllViews();

        if (_state.Tunnels.Count == 0)
        {
            var empty = new LinearLayout(this)
            {
                Orientation = Orientation.Vertical,
            };
            empty.SetGravity(GravityFlags.CenterHorizontal);
            empty.SetPadding(Dp(16), Dp(24), Dp(16), Dp(8));

            var icon = new ImageView(this)
            {
                LayoutParameters = new LinearLayout.LayoutParams(Dp(48), Dp(48)),
                ContentDescription = GetString(Resource.String.empty_tunnels_title),
            };
            icon.SetImageResource(Resource.Drawable.ic_tunnel);
            icon.Alpha = 0.45f;
            empty.AddView(icon);

            var title = new TextView(this)
            {
                Text = GetString(Resource.String.empty_tunnels_title),
                TextSize = 15,
                Gravity = GravityFlags.Center,
            };
            title.SetTextColor(ColorRes(Resource.Color.colorOnSurface));
            title.SetTypeface(null, Android.Graphics.TypefaceStyle.Bold);
            title.SetPadding(0, Dp(8), 0, 0);
            empty.AddView(title);

            var body = new TextView(this)
            {
                Text = GetString(Resource.String.empty_tunnels_body),
                TextSize = 13,
                Gravity = GravityFlags.Center,
            };
            body.SetTextColor(ColorRes(Resource.Color.colorOnSurfaceVariant));
            empty.AddView(body);

            _tunnelList.AddView(empty);
            return;
        }

        for (var i = 0; i < _state.Tunnels.Count; i++)
        {
            _tunnelList.AddView(BuildTunnelEditor(_state.Tunnels[i], i));
        }
    }

    private View BuildTunnelEditor(WsTuneCommon.Models.TunnelConfigDto tunnel, int index)
    {
        var card = new MaterialCardView(this)
        {
            LayoutParameters = new LinearLayout.LayoutParams(-1, ViewGroup.LayoutParams.WrapContent) { TopMargin = Dp(8) },
            Radius = Dp(12),
            StrokeWidth = Dp(1),
            StrokeColor = ColorRes(Resource.Color.colorOutline),
            CardElevation = 0,
        };
        card.SetContentPadding(Dp(16), Dp(8), Dp(16), Dp(16));

        var box = new LinearLayout(this) { Orientation = Orientation.Vertical };
        card.AddView(box, new ViewGroup.LayoutParams(-1, ViewGroup.LayoutParams.WrapContent));

        // header: name preview + remove
        var head = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        head.SetGravity(GravityFlags.CenterVertical);
        var headName = new TextView(this)
        {
            TextSize = 15,
            LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f),
        };
        headName.SetTextColor(ColorRes(Resource.Color.colorOnSurface));
        headName.SetTypeface(null, Android.Graphics.TypefaceStyle.Bold);
        headName.Text = string.IsNullOrWhiteSpace(tunnel.Name) ? $"Tunnel {index + 1}" : tunnel.Name;
        head.AddView(headName);

        var rIdx = index;
        var remove = IconButton(Resource.String.cd_delete_tunnel, Resource.Drawable.ic_delete);
        remove.Click += (_, _) => RemoveTunnel(rIdx);
        head.AddView(remove);
        box.AddView(head);

        // Name / Destination row
        var row1 = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var nameTil = OutlinedField(GetString(Resource.String.hint_name), InputTypes.ClassText);
        nameTil.LayoutParameters = HalfWidth();
        var nameEdit = (TextInputEditText)nameTil.EditText!;
        nameEdit.Text = tunnel.Name;
        nameEdit.TextChanged += (_, _) =>
        {
            tunnel.Name = nameEdit.Text ?? "";
            headName.Text = string.IsNullOrWhiteSpace(tunnel.Name) ? $"Tunnel {index + 1}" : tunnel.Name;
            SaveState();
        };
        row1.AddView(nameTil);

        var destTil = OutlinedField(GetString(Resource.String.hint_destination), InputTypes.ClassText);
        destTil.LayoutParameters = HalfWidth();
        var destEdit = (TextInputEditText)destTil.EditText!;
        destEdit.Text = tunnel.Destination;
        destEdit.TextChanged += (_, _) => { tunnel.Destination = destEdit.Text ?? ""; SaveState(); };
        row1.AddView(destTil);
        box.AddView(row1);

        // Listen port / target port row
        var row2 = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var listenTil = OutlinedField(GetString(Resource.String.hint_listen_port), InputTypes.ClassNumber);
        listenTil.LayoutParameters = HalfWidth();
        var listenEdit = (TextInputEditText)listenTil.EditText!;
        listenEdit.Text = tunnel.ListenPort.ToString();
        listenEdit.TextChanged += (_, _) =>
        {
            if (int.TryParse(listenEdit.Text, out var p)) tunnel.ListenPort = p;
            SaveState();
        };
        row2.AddView(listenTil);

        var targetPortTil = OutlinedField(GetString(Resource.String.hint_target_port), InputTypes.ClassNumber);
        targetPortTil.LayoutParameters = HalfWidth();
        var targetPortEdit = (TextInputEditText)targetPortTil.EditText!;
        targetPortEdit.Text = tunnel.TargetPort.ToString();
        targetPortEdit.TextChanged += (_, _) =>
        {
            if (int.TryParse(targetPortEdit.Text, out var p)) tunnel.TargetPort = p;
            SaveState();
        };
        row2.AddView(targetPortTil);
        box.AddView(row2);

        // Target host + protocol chips
        var row3 = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row3.SetGravity(GravityFlags.CenterVertical);

        var hostTil = OutlinedField(GetString(Resource.String.hint_target_host), InputTypes.ClassText);
        hostTil.LayoutParameters = HalfWidth();
        var hostEdit = (TextInputEditText)hostTil.EditText!;
        hostEdit.Text = tunnel.TargetHost;
        hostEdit.TextChanged += (_, _) => { tunnel.TargetHost = hostEdit.Text ?? ""; SaveState(); };
        row3.AddView(hostTil);

        var protoCol = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { LeftMargin = Dp(4) },
        };
        protoCol.AddView(FieldLabel("Protocol"));

        var chipGroup = new ChipGroup(this)
        {
            SingleSelection = true,
            SelectionRequired = true,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent),
        };
        chipGroup.SetPadding(0, Dp(2), 0, 0);

        var tcp = ProtocolChip("TCP");
        var udp = ProtocolChip("UDP");
        chipGroup.AddView(tcp);
        chipGroup.AddView(udp);

        var applying = true;
        tcp.CheckedChange += (_, e) =>
        {
            if (applying || !e.IsChecked) return;
            tunnel.Protocol = "TCP";
            SaveState();
        };
        udp.CheckedChange += (_, e) =>
        {
            if (applying || !e.IsChecked) return;
            tunnel.Protocol = "UDP";
            SaveState();
        };
        if (tunnel.Protocol == "UDP")
            udp.Checked = true;
        else
            tcp.Checked = true;
        applying = false;

        protoCol.AddView(chipGroup);
        row3.AddView(protoCol);
        box.AddView(row3);

        return card;
    }

    private LinearLayout.LayoutParams HalfWidth()
        => new(0, ViewGroup.LayoutParams.WrapContent, 1f) { RightMargin = Dp(8) };

    private void RemoveTunnel(int index)
    {
        if (index < 0 || index >= _state.Tunnels.Count) return;
        var removed = _state.Tunnels[index];
        _state.Tunnels.RemoveAt(index);
        SaveState();
        RenderTunnels();

        ShowSnack($"Removed “{removed.Name}”", () =>
        {
            if (index <= _state.Tunnels.Count)
                _state.Tunnels.Insert(index, removed);
            else
                _state.Tunnels.Add(removed);
            SaveState();
            RenderTunnels();
        });
    }

    // -------------------------------------------------------------- control --

    private View BuildBottomBar()
    {
        var bar = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new LinearLayout.LayoutParams(-1, ViewGroup.LayoutParams.WrapContent),
        };
        bar.SetGravity(GravityFlags.CenterVertical);
        bar.SetPadding(Dp(16), Dp(12), Dp(16), Dp(16));
        bar.SetBackgroundColor(ColorRes(Resource.Color.colorSurface));
        bar.Elevation = Dp(8);

        _statusChip = new Chip(this)
        {
            Text = GetString(Resource.String.status_stopped),
            LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) { RightMargin = Dp(12) },
            ContentDescription = GetString(Resource.String.cd_status),
        };
        _statusChip.SetTextColor(ColorRes(Resource.Color.colorOnSurfaceVariant));
        _statusChip.ChipBackgroundColor = Android.Content.Res.ColorStateList.ValueOf(
            ColorRes(Resource.Color.colorSurfaceContainer));
        bar.AddView(_statusChip);

        _toggleButton = new MaterialButton(this)
        {
            Text = GetString(Resource.String.btn_start),
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent),
        };
        _toggleButton.Click += OnToggleClicked;
        bar.AddView(_toggleButton);

        return bar;
    }

    private View BuildLogCard()
    {
        var card = NewCard(Resource.String.card_log);

        var actions = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new LinearLayout.LayoutParams(-1, ViewGroup.LayoutParams.WrapContent),
        };
        actions.SetGravity(GravityFlags.End);
        var clearBtn = new MaterialButton(this)
        {
            Text = GetString(Resource.String.btn_clear_log),
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent),
        };
        clearBtn.InsetTop = 0;
        clearBtn.InsetBottom = 0;
        clearBtn.SetTextColor(ColorRes(Resource.Color.colorPrimary));
        clearBtn.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(
            ColorRes(Resource.Color.colorSurfaceContainer));
        clearBtn.Click += (_, _) =>
        {
            if (_logText is not null) _logText.Text = string.Empty;
        };
        actions.AddView(clearBtn);
        card.AddView(actions);

        _logText = new TextView(this)
        {
            TextSize = 12,
            Typeface = Android.Graphics.Typeface.Monospace,
            Text = "",
        };
        _logText.SetTextColor(ColorRes(Resource.Color.colorOnSurface));

        _logScroll = new ScrollView(this)
        {
            LayoutParameters = new LinearLayout.LayoutParams(-1, Dp(200)),
        };
        _logScroll.AddView(_logText);
        card.AddView(_logScroll);
        return card;
    }

    // ------------------------------------------------------------- helpers --

    /// <summary>
    /// MaterialCardView with a vertical content column (title + room for children).
    /// Returns the column so AddView stacks under the title (cards are FrameLayouts).
    /// </summary>
    private LinearLayout NewCard(int titleRes)
    {
        var card = new MaterialCardView(this)
        {
            LayoutParameters = new LinearLayout.LayoutParams(-1, ViewGroup.LayoutParams.WrapContent) { TopMargin = Dp(12) },
            Radius = Dp(16),
            StrokeWidth = Dp(0),
            CardElevation = Dp(1),
            CardBackgroundColor = Android.Content.Res.ColorStateList.ValueOf(ColorRes(Resource.Color.colorSurface)),
        };
        card.SetContentPadding(Dp(16), Dp(12), Dp(16), Dp(16));

        var column = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new ViewGroup.LayoutParams(-1, ViewGroup.LayoutParams.WrapContent),
        };
        card.AddView(column);

        var head = new TextView(this) { Text = GetString(titleRes), TextSize = 17 };
        head.SetTextColor(ColorRes(Resource.Color.colorOnSurface));
        head.SetTypeface(null, Android.Graphics.TypefaceStyle.Bold);
        head.SetPadding(0, 0, 0, Dp(4));
        column.AddView(head);

        return column;
    }

    private TextView FieldLabel(string text)
    {
        var label = new TextView(this) { Text = text, TextSize = 12 };
        label.SetTextColor(ColorRes(Resource.Color.colorOnSurfaceVariant));
        label.SetPadding(0, Dp(6), 0, Dp(2));
        return label;
    }

    private Chip ProtocolChip(string protocol)
    {
        return new Chip(this)
        {
            Text = protocol,
            Checkable = true,
        };
    }

    private ImageButton IconButton(int contentDesc, int icon)
    {
        var b = new ImageButton(this)
        {
            LayoutParameters = new LinearLayout.LayoutParams(Dp(48), Dp(48)) { LeftMargin = Dp(4) },
            ContentDescription = GetString(contentDesc),
            ScaleType = Android.Widget.ScaleType.FitCenter,
        };
        b.SetImageResource(icon);
        b.SetBackgroundColor(Android.Graphics.Color.Transparent);
        b.SetPadding(Dp(12), Dp(12), Dp(12), Dp(12));
        return b;
    }

    /// <summary>Outlined TextInputLayout + TextInputEditText (floating label), full width.</summary>
    private TextInputLayout OutlinedField(string hint, InputTypes inputTypes)
    {
        // defStyleAttr=0 → theme textInputStyle (set in MainTheme). Passing a style
        // ID as defStyleAttr is incorrect for Android and can break ThemeEnforcement.
        var til = new TextInputLayout(this)
        {
            LayoutParameters = new LinearLayout.LayoutParams(-1, ViewGroup.LayoutParams.WrapContent)
            {
                BottomMargin = Dp(4),
            },
            Hint = hint,
        };
        var edit = new TextInputEditText(this)
        {
            LayoutParameters = new ViewGroup.LayoutParams(-1, ViewGroup.LayoutParams.WrapContent),
            InputType = inputTypes,
        };
        edit.SetSingleLine(true);
        til.AddView(edit);
        return til;
    }

    // ------------------------------------------------------------ profiles --

    private void RenderProfiles()
    {
        if (_profileDropdown is null) return;
        _profileSilent = true;
        var names = _state.Profiles.Select(p => p.Name).ToArray();
        _profileDropdown.SetSimpleItems(names);
        var active = _state.ActiveProfile ?? "";
        if (!string.IsNullOrEmpty(active))
            _profileDropdown.SetText(active, false);
        _profileSilent = false;
    }

    private void OnProfileClick(object? sender, AdapterView.ItemClickEventArgs e)
    {
        if (_profileSilent || e.Position < 0 || e.Position >= _state.Profiles.Count)
            return;
        var profile = _state.Profiles[e.Position];
        _state.SignalR = profile.SignalR;
        _state.ActiveProfile = profile.Name;
        if (_urlEdit is not null)
            _urlEdit.Text = profile.SignalR;
        SaveState();
        ShowSnack($"Loaded “{profile.Name}”");
    }

    private void PromptSaveProfile()
    {
        var input = new EditText(this)
        {
            Text = _state.ActiveProfile ?? "",
            Hint = GetString(Resource.String.hint_profile),
            InputType = InputTypes.ClassText,
        };
        input.SetSingleLine(true);

        var padding = Dp(20);
        var wrapper = new LinearLayout(this) { Orientation = Orientation.Vertical };
        wrapper.SetPadding(padding, padding / 2, padding, 0);
        wrapper.AddView(input);

        new MaterialAlertDialogBuilder(this)
            .SetTitle(Resource.String.title_save_profile)
            .SetMessage(Resource.String.msg_save_profile)
            .SetView(wrapper)
            .SetPositiveButton(Resource.String.btn_save, (_, _) =>
            {
                var name = input.Text?.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    ShowSnack(GetString(Resource.String.msg_name_required));
                    return;
                }
                var url = _urlEdit?.Text?.Trim() ?? "";
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
                ShowSnack($"Saved “{name}”");
            })
            .SetNegativeButton(Resource.String.btn_cancel, (_, _) => { })
            .Show();
    }

    private void DeleteActiveProfile()
    {
        if (_state.ActiveProfile is null)
        {
            ShowSnack(GetString(Resource.String.msg_no_active_profile));
            return;
        }
        var name = _state.ActiveProfile;
        new MaterialAlertDialogBuilder(this)
            .SetTitle(Resource.String.title_delete_profile)
            .SetMessage(GetString(Resource.String.msg_delete_profile))
            .SetPositiveButton(Resource.String.btn_delete, (_, _) =>
            {
                _state.Profiles.RemoveAll(p => p.Name == name);
                _state.ActiveProfile = null;
                SaveState();
                RenderProfiles();
            })
            .SetNegativeButton(Resource.String.btn_cancel, (_, _) => { })
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
            ShowSnack(GetString(Resource.String.msg_url_invalid));
            return;
        }
        if (!ValidateTunnels(out var error))
        {
            ShowSnack(error);
            return;
        }

        // One-time identity setup: lock the field on first successful start.
        if (!_state.IdentityLocked)
        {
            _state.IdentityLocked = true;
            ApplyIdentityLockState();
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
            error = GetString(Resource.String.msg_no_tunnels);
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

    private void ShowSnack(string message, Action? undo = null)
    {
        if (_root is null)
        {
            Toast.MakeText(this, message, ToastLength.Short)?.Show();
            return;
        }
        var snack = Snackbar.Make(_root, new Java.Lang.String(message),
            undo is null ? Snackbar.LengthShort : Snackbar.LengthLong);
        if (undo is not null)
            snack.SetAction(new Java.Lang.String(GetString(Resource.String.btn_undo)), Click(undo));
        snack.Show();
    }

    private void SaveState()
    {
        try { _state.Save(this); } catch { /* persistence must never crash the UI */ }
    }

    private void RefreshState()
    {
        RunOnUiThread(() =>
        {
            var running = ListenerService.IsRunning;
            if (_toggleButton is not null)
            {
                _toggleButton.Text = GetString(running ? Resource.String.btn_stop : Resource.String.btn_start);
                _toggleButton.SetIconResource(running ? Resource.Drawable.ic_stop : Resource.Drawable.ic_play);
                _toggleButton.IconGravity = MaterialButton.IconGravityTextStart;
                _toggleButton.IconPadding = Dp(8);
                _toggleButton.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(
                    running ? ColorRes(Resource.Color.colorError) : ColorRes(Resource.Color.colorPrimary));
                _toggleButton.SetTextColor(ColorRes(running ? Resource.Color.colorOnError : Resource.Color.colorOnPrimary));
            }
            if (_statusChip is not null)
            {
                _statusChip.Text = running ? ListenerService.Status : GetString(Resource.String.status_stopped);
                _statusChip.SetTextColor(ColorRes(running ? Resource.Color.colorOnSuccess : Resource.Color.colorOnSurfaceVariant));
                _statusChip.ChipBackgroundColor = Android.Content.Res.ColorStateList.ValueOf(
                    running ? ColorRes(Resource.Color.colorSuccess) : ColorRes(Resource.Color.colorSurfaceContainer));
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
