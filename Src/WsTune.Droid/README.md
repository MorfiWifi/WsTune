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

1. **Host SignalR URL** — full URL of your Host endpoint, e.g.
   `https://your-host/sample-endpoint`.
2. **Identity** — this device name (a unique suffix is added automatically, same as desktop).
3. **Tunnels** — JSON array, same shape as `appsettings.listener.json`:
   `Name`, `Protocol` (`TCP`), `ListenPort` (1024–65535, Android blocks < 1024),
   `TargetHost`, `TargetPort`, `Destination` (the Server identity).

Press **Start listener**, then connect your VNC/RDP client to `127.0.0.1:<ListenPort>`
on the phone (or use it as the entry point defined by your setup).

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
as an artifact (`wstune-listener-apk`). On `v*` tags / releases it also attaches the
APK to the GitHub Release.

## Notes / limits

- Trimming and AOT are **off** on purpose: SignalR JSON source-gen + the
  `linux-bionic` lessons in `release.yml` show trimming breaks this stack.
- Only **TCP** tunnels are exposed in v1 (same as the desktop listener path used here).
- Minimum Android 8.0 (API 26) for notification channels + foreground-service types.
