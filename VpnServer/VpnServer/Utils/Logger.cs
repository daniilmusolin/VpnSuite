using System.Collections.Concurrent;

namespace VpnCore.Utils;

public enum LogLevel {
    Debug, Info, Warning, Error, Critical
}

public sealed class Logger : IDisposable {
    private static Logger? _instance;
    private static readonly object _lock = new();
    private readonly string _logDirectory;
    private readonly BlockingCollection<LogEntry> _logQueue;
    private readonly CancellationTokenSource _cts;
    private readonly Task _workerTask;
    private bool _disposed;

    public static Logger Instance {
        get {
            if (_instance == null) {
                lock (_lock) {
                    _instance ??= new Logger();
                }
            }
            return _instance;
        }
    }

    private Logger() {
        _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
        if (!Directory.Exists(_logDirectory))
            Directory.CreateDirectory(_logDirectory);

        _logQueue = new BlockingCollection<LogEntry>(new ConcurrentQueue<LogEntry>(), 10000);
        _cts = new CancellationTokenSource();
        _workerTask = Task.Run(ProcessLogQueue);

        Info("Logger initialized");
    }

    public void Log(LogLevel level, string message, string? source = null) {
        if (string.IsNullOrEmpty(message)) return;

        var entry = new LogEntry {
            Timestamp = DateTime.Now,
            Level = level,
            Message = message,
            Source = source ?? GetCallerInfo(),
            ThreadId = Thread.CurrentThread.ManagedThreadId
        };

        _logQueue.Add(entry);
    }

    public void Debug(string message, string? source = null) => Log(LogLevel.Debug, message, source);
    public void Info(string message, string? source = null) => Log(LogLevel.Info, message, source);
    public void Warning(string message, string? source = null) => Log(LogLevel.Warning, message, source);
    public void Error(string message, string? source = null) => Log(LogLevel.Error, message, source);
    public void Critical(string message, string? source = null) => Log(LogLevel.Critical, message, source);

    private async Task ProcessLogQueue() {
        var currentDate = DateTime.Now.Date;
        var currentLogFile = GetLogFilePath(currentDate);

        foreach (var entry in _logQueue.GetConsumingEnumerable(_cts.Token)) {
            try {
                if (entry.Timestamp.Date != currentDate) {
                    currentDate = entry.Timestamp.Date;
                    currentLogFile = GetLogFilePath(currentDate);
                }

                var logLine = $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{entry.Level}] [{entry.Source}] {entry.Message}";
                await File.AppendAllTextAsync(currentLogFile, logLine + Environment.NewLine);

                Console.WriteLine(logLine);
            } catch { }
        }
    }

    private string GetCallerInfo() {
        try {
            var stackTrace = new System.Diagnostics.StackTrace();
            var frame = stackTrace.GetFrame(3);
            var method = frame?.GetMethod();
            var type = method?.DeclaringType;
            return type != null ? $"{type.Name}.{method?.Name}" : "Unknown";
        } catch { return "Unknown"; }
    }

    private string GetLogFilePath(DateTime date) =>
        Path.Combine(_logDirectory, $"vpn_{date:yyyyMMdd}.log");

    public void Dispose() {
        if (!_disposed) {
            _cts.Cancel();
            _workerTask.Wait(5000);
            _logQueue.Dispose();
            _cts.Dispose();
            _disposed = true;
        }
    }

    private class LogEntry {
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; } = "";
        public string Source { get; set; } = "";
        public int ThreadId { get; set; }
    }
}