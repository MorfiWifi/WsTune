using Microsoft.Extensions.Logging;

namespace WsTune.Droid;

/// <summary>
/// Forwards Microsoft.Extensions.Logging output to logcat AND to an optional
/// in-app log view (via the OnLog event marshalled by the subscriber).
/// </summary>
public sealed class AndroidLoggerProvider : ILoggerProvider
{
    public event Action<string>? OnLog;

    public ILogger CreateLogger(string categoryName) => new AndroidLogger(categoryName, OnLog);

    public void Dispose() { }

    private sealed class AndroidLogger : ILogger
    {
        private readonly string _category;
        private readonly Action<string>? _sink;

        public AndroidLogger(string category, Action<string>? sink)
        {
            _category = category;
            _sink = sink;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = $"{DateTime.Now:HH:mm:ss} [{logLevel}] {_category}: {formatter(state, exception)}";
            if (exception is not null)
                message += Environment.NewLine + exception;
            try
            {
                Android.Util.Log.WriteLine(ToPriority(logLevel), "WsTune", message);
                System.Diagnostics.Debug.WriteLine(message);
            }
            catch { /* logging must never crash the tunnel */ }
            try { _sink?.Invoke(message); } catch { /* ignore */ }
        }

        private static Android.Util.LogPriority ToPriority(LogLevel level) => level switch
        {
            LogLevel.Trace => Android.Util.LogPriority.Verbose,
            LogLevel.Debug => Android.Util.LogPriority.Debug,
            LogLevel.Information => Android.Util.LogPriority.Info,
            LogLevel.Warning => Android.Util.LogPriority.Warn,
            LogLevel.Error => Android.Util.LogPriority.Error,
            LogLevel.Critical => Android.Util.LogPriority.Error,
            _ => Android.Util.LogPriority.Info,
        };
    }
}
