using System.Diagnostics;
using System.Text;

namespace DMAW_DND
{
    internal enum ActivitySeverity
    {
        Trace,
        Debug,
        Info,
        Warn,
        Error,
        Critical
    }

    /// <summary>
    /// Central activity log: timestamped lines to a daily file, optional console mirror, and capture of stdout.
    /// </summary>
    internal static class ActivityLog
    {
        private static readonly object Gate = new();
        private static StreamWriter? _writer;
        private static TextWriter? _originalOut;
        private static TextWriter? _originalErr;
        private static bool _installedTee;
        private static bool _enabled = true;
        private static ActivitySeverity _minSeverity = ActivitySeverity.Trace;
        private static bool _mirrorToConsoleInDebug = true;
        private static bool _mirrorToConsole;
        private static bool _logEveryFrame;
        private static bool _logMouseMoves;
        private static bool _radarDiagnostics = true;
        private static string _logDirectory = "Logs";
        private static string? _currentLogPath;
        private static int _shutdownDone;

        public static bool LogEveryFrame => _logEveryFrame;
        public static bool LogMouseMoves => _logMouseMoves;

        public static void InitializeEarly()
        {
            lock (Gate)
            {
                _originalOut = Console.Out;
                _originalErr = Console.Error;
                EnsureWriterUnlocked();
                if (!_installedTee)
                {
                    Console.SetOut(new TeeTextWriter(_originalOut, AppendConsoleLine));
                    Console.SetError(new TeeTextWriter(_originalErr, line => AppendConsoleLine("ConsoleError", line)));
                    _installedTee = true;
                }
            }

            Info("Startup", $"Process started (PID={Environment.ProcessId}, version={Environment.Version})");
        }

        public static void ConfigureFrom(ConfigStructure config)
        {
            lock (Gate)
            {
                _enabled = config.ActivityLoggingEnabled;
                _logDirectory = string.IsNullOrWhiteSpace(config.ActivityLogDirectory)
                    ? "Logs"
                    : config.ActivityLogDirectory.Trim();
                _minSeverity = ParseSeverity(config.ActivityLogMinimumLevel);
                _logEveryFrame = config.ActivityLogLogEveryFrame;
                _logMouseMoves = config.ActivityLogLogMouseMoves;
                _mirrorToConsoleInDebug = config.ActivityLogMirrorToConsoleInDebug;
                _mirrorToConsole = config.ActivityLogMirrorToConsole;
                _radarDiagnostics = config.ActivityLogRadarDiagnostics;
                EnsureWriterUnlocked();
            }

            Info("Logging", $"Configured enabled={_enabled} dir={_logDirectory} min={_minSeverity} everyFrame={_logEveryFrame} mouseMoves={_logMouseMoves} mirrorConsole={_mirrorToConsole} radarDiag={_radarDiagnostics}");
        }

        public static bool RadarDiagnostics => _radarDiagnostics;

        // Default category "App" — use for quick calls: ActivityLog.Info("message")
        public static void Trace(string message) => Trace("App", message);
        public static void Debug(string message) => Debug("App", message);
        public static void Info(string message) => Info("App", message);
        public static void Warn(string message) => Warn("App", message);
        public static void Error(string message) => Error("App", message);
        public static void Critical(string message) => Critical("App", message);

        public static void Trace(string category, string message) => Write(ActivitySeverity.Trace, category, message, mirror: true);
        public static void Debug(string category, string message) => Write(ActivitySeverity.Debug, category, message, mirror: true);
        public static void Info(string category, string message) => Write(ActivitySeverity.Info, category, message, mirror: true);
        public static void Warn(string category, string message) => Write(ActivitySeverity.Warn, category, message, mirror: true);
        public static void Error(string category, string message) => Write(ActivitySeverity.Error, category, message, mirror: true);
        public static void Critical(string category, string message) => Write(ActivitySeverity.Critical, category, message, mirror: true);

        public static void Exception(string category, Exception ex, string? context = null)
        {
            var msg = context != null ? $"{context}: {ex}" : ex.ToString();
            Write(ActivitySeverity.Error, category, msg, mirror: true);
        }

        public static void CriticalException(string category, Exception ex, string? context = null)
        {
            var msg = context != null ? $"{context}: {ex}" : ex.ToString();
            Write(ActivitySeverity.Critical, category, msg, mirror: true);
        }

        private static void AppendConsoleLine(string line) => Write(ActivitySeverity.Info, "Console", line, mirror: false);

        private static void AppendConsoleLine(string category, string line) =>
            Write(ActivitySeverity.Info, category, line, mirror: false);

        private static bool BypassesMinimumFilter(ActivitySeverity severity) =>
            severity >= ActivitySeverity.Warn || severity == ActivitySeverity.Critical;

        private static void Write(ActivitySeverity severity, string category, string message, bool mirror)
        {
            if (!_enabled && !BypassesMinimumFilter(severity))
                return;

            if (!BypassesMinimumFilter(severity) && severity < _minSeverity)
                return;

            var line = $"{DateTime.UtcNow:O}\t{severity}\t[{Environment.CurrentManagedThreadId}]\t{category}\t{message}";

            lock (Gate)
            {
                try
                {
                    EnsureWriterUnlocked();
                    _writer?.WriteLine(line);
                }
                catch
                {
                    // Never throw from logging
                }
            }

            var mirrorConsole =
#if DEBUG
                mirror && (_mirrorToConsoleInDebug || _mirrorToConsole);
#else
                mirror && _mirrorToConsole;
#endif
            if (mirrorConsole && _originalOut != null)
            {
                try
                {
                    _originalOut.WriteLine($"[{severity}] [{category}] {message}");
                }
                catch
                {
                    // ignored
                }
            }
        }

        private static void EnsureWriterUnlocked()
        {
            if (!_enabled)
            {
                _writer?.Dispose();
                _writer = null;
                _currentLogPath = null;
                return;
            }

            var baseDir = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, _logDirectory));
            Directory.CreateDirectory(baseDir);
            var fileName = Path.Combine(baseDir, $"activity-{DateTime.UtcNow:yyyyMMdd}.log");

            if (_writer != null && string.Equals(_currentLogPath, fileName, StringComparison.OrdinalIgnoreCase))
                return;

            _writer?.Dispose();
            _writer = null;
            _currentLogPath = fileName;

            _writer = new StreamWriter(new FileStream(fileName, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = false
            };
        }

        private static ActivitySeverity ParseSeverity(string? s)
        {
            if (Enum.TryParse<ActivitySeverity>(s, ignoreCase: true, out var v))
                return v;
            return ActivitySeverity.Trace;
        }

        public static void Shutdown()
        {
            if (Interlocked.Exchange(ref _shutdownDone, 1) != 0)
                return;

            lock (Gate)
            {
                try
                {
                    if (_writer != null)
                    {
                        _writer.WriteLine(
                            $"{DateTime.UtcNow:O}\t{ActivitySeverity.Info}\t[{Environment.CurrentManagedThreadId}]\tShutdown\tActivity log shutting down");
                        _writer.Flush();
                    }
                }
                catch
                {
                    // ignored
                }
                finally
                {
                    _writer?.Dispose();
                    _writer = null;
                    _currentLogPath = null;
                }
            }
        }

        private sealed class TeeTextWriter : TextWriter
        {
            private readonly TextWriter _inner;
            private readonly Action<string> _onLine;
            private readonly StringBuilder _buf = new();

            public TeeTextWriter(TextWriter inner, Action<string> onLine)
            {
                _inner = inner;
                _onLine = onLine;
            }

            public override Encoding Encoding => _inner.Encoding;

            public override void Write(char value)
            {
                _inner.Write(value);
                if (value == '\n')
                    FlushBuffer();
                else if (value != '\r')
                    _buf.Append(value);
            }

            public override void Write(string? value)
            {
                if (value == null || value.Length == 0)
                {
                    _inner.Write(value);
                    return;
                }

                _inner.Write(value);
                foreach (var c in value)
                {
                    if (c == '\n')
                        FlushBuffer();
                    else if (c != '\r')
                        _buf.Append(c);
                }
            }

            public override void WriteLine(string? value)
            {
                _inner.WriteLine(value);
                if (_buf.Length > 0 || value != null)
                {
                    if (value != null)
                        _buf.Append(value);
                    _onLine(_buf.ToString());
                    _buf.Clear();
                }
                else
                {
                    _onLine(string.Empty);
                }
            }

            private void FlushBuffer()
            {
                if (_buf.Length == 0)
                    return;
                _onLine(_buf.ToString());
                _buf.Clear();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && _buf.Length > 0)
                    FlushBuffer();
                base.Dispose(disposing);
            }
        }
    }
}
