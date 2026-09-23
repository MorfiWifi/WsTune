# WsTune.Droid — Android APK for the WsTune Listener (client)

.NET for Android (`net10.0-android`) port of `WsTuneCli.Listener`.
Same wire protocol (SignalR `/sample-endpoint` + `TcpFwV4` tunnels), wrapped in a
single-screen app + foreground service so the tunnel survives in the background.

## What it does

- Connects to the WsTune **Host** over WebSockets (SignalR), same as the desktop listener.
- Listens on local TCP ports on the phone and forwards them through the Host to the
  destination **Server** (`Destination` identity).
- Runs inside `ListenerService` (foreground service + notification) so Android does
  not kill it when the screen turns off.

## Configure (in the app)

Card-based Material Design 3 UI (top app bar, outlined fields, snackbars,
dark mode) — no raw JSON editing — everything is persisted to
`SharedPreferences` (`AppState.cs`) and survives restarts:

1. **Connection card**
   - **Host SignalR URL** — full URL of your Host endpoint, e.g.
     `https://your-host/sample-endpoint` (outlined field with clear icon).
   - **Profiles** — exposed-dropdown of saved base addresses; save/delete
     via icon buttons (`MaterialAlertDialog` for names).
   - **Identity** — this device name; editable **only until the first Start**,
     then locked for the installation (helper text + lock end icon).
2. **Tunnels card** — nested Material cards per tunnel: **Name**, **Destination**,
   **Listen port** (1024–65535, Android blocks < 1024), **Target host**,
   **Target port**, **Protocol** (TCP/UDP choice chips). Add with **Add tunnel**;
   remove with a delete icon — snackbar offers **Undo**.
3. **Sticky bottom bar** — status chip + filled **Start/Stop** button (error tint
   while running).
4. **Log card** — rolling tunnel log with **Clear** (also mirrored to logcat
   tag `WsTune`).

Press **Start**, then connect your VNC/RDP client to `127.0.0.1:<ListenPort>`
on the phone (or use it as the entry point defined by your setup).

Theme is `Theme.Material3.DayNight` (follows system light/dark), with an
adaptive launcher **icon** and a brand **splash screen**
(`windowBackground` on API 26–30, platform SplashScreen API on 31+).
UI pieces: `MaterialToolbar`, `TextInputLayout` (floating labels),
`MaterialCardView`, `MaterialButton`, `Chip`/`ChipGroup`, `Snackbar`,
`MaterialAlertDialogBuilder` via `Xamarin.Google.Android.Material`.

## Build locally

```bash
# first time only
dotnet workload install android

# debug APK (fast, installable, signed with debug key)
dotnet build Src/WsTune.Droid/WsTune.Droid.csproj -c Debug -f net10.0-android

# release APK (what CI uploads)
dotnet build Src/WsTune.Droid/WsTune.Droid.csproj -c Release -f net10.0-android
```

APK output: `Src/WsTune.Droid/bin/Release/net10.0-android/*-Signed.apk`
(`AndroidPackageFormat=apk`, universal `arm64-v8a;armeabi-v7a`).

Install:

```bash
adb install -r <path-to>-Signed.apk
```

## CI

`.github/workflows/android-apk.yml` builds the APK on every push / PR and uploads it
as an artifact (`wstune-listener-apk`). On **push** events (main / `v*` tags) it also
**attaches the APK files to the GitHub Release** for the resolved nbgv version
(`v<version>`), so downloads live in the repo's Releases section next to the
desktop bundles from `release.yml`.

## Notes / limits

- Trimming and AOT are **off** on purpose: SignalR JSON source-gen + the
  `linux-bionic` lessons in `release.yml` show trimming breaks this stack.
- Only **TCP** tunnels are exposed in v1 (same as the desktop listener path used here).
- Minimum Android 8.0 (API 26) for notification channels + foreground-service types.
