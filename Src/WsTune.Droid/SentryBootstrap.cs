using Sentry;

namespace WsTune.Droid;

/// <summary>
/// Early Sentry init so crashes in Application/Activity startup are captured.
/// DSN is not a secret (public in the APK); keep it stable across builds.
/// </summary>
internal static class SentryBootstrap
{
    public const string Dsn =
        "https://83461aab0d619a9b193a6346973e3aaa@o503229.ingest.us.sentry.io/4512136905162752";

    private static int _initialized;

    public static void Init()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
            return;

        try
        {
            SentrySdk.Init(options =>
            {
                options.Dsn = Dsn;
                options.Debug = false;
                options.AutoSessionTracking = true;
                options.IsGlobalModeEnabled = true;
                options.Release = "wstune-listener@1.1.31";
                options.Environment = "production";
                options.DefaultTags["app"] = "wstune-listener";
            });

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                    SentrySdk.CaptureException(ex);
                else
                    SentrySdk.CaptureMessage("Unhandled: " + e.ExceptionObject);
            };

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                SentrySdk.CaptureException(e.Exception);
                e.SetObserved();
            };
        }
        catch
        {
            // Sentry must never take the app down.
        }
    }

    /// <summary>Report a caught exception (UI paths already log to logcat).</summary>
    public static void Capture(Exception ex, string? hint = null)
    {
        try
        {
            if (hint is not null)
                SentrySdk.ConfigureScope(s => s.SetTag("hint", hint));
            SentrySdk.CaptureException(ex);
        }
        catch { /* ignore */ }
    }
}
