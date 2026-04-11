using System.Collections.Concurrent;

namespace VpnCore.Utils {
    /// <summary>
    /// Уровни логирования
    /// </summary>
    public enum LogLevel {
        Debug,      // Отладочная информация (только для разработки)
        Info,       // Информационные сообщения
        Warning,    // Предупреждения (не критично, но стоит обратить внимание)
        Error,      // Ошибки (функциональность нарушена)
        Critical    // Критические ошибки (приложение может упасть)
    }

    /// <summary>
    /// Асинхронный логгер с поддержкой нескольких аппендеров
    /// Использует паттерн Singleton для единого доступа
    /// Потокобезопасен и не блокирует основной поток
    /// </summary>
    public sealed class Logger : IDisposable {
        private static Logger _instance;
        private static readonly object _lock = new object();

        private readonly string _logDirectory;
        private readonly BlockingCollection<LogEntry> _logQueue;
        private readonly CancellationTokenSource _cts;
        private readonly Task _workerTask;
        private bool _disposed;

        // Событие для уведомления о новых логах (для UI)
        public event Action<string, LogLevel> OnLogWritten;

        /// <summary>
        /// Singleton instance
        /// </summary>
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
            // Создаем директорию для логов
            _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            if (!Directory.Exists(_logDirectory))
                Directory.CreateDirectory(_logDirectory);

            _logQueue = new BlockingCollection<LogEntry>(new ConcurrentQueue<LogEntry>(), 10000);
            _cts = new CancellationTokenSource();
            _workerTask = Task.Run(ProcessLogQueue);

            Log(LogLevel.Info, "Logger initialized");
        }

        /// <summary>
        /// Логирование сообщения
        /// </summary>
        public void Log(LogLevel level, string message, string source = null) {
            if (string.IsNullOrEmpty(message)) return;

            var entry = new LogEntry {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message,
                Source = source ?? GetCallerInfo(),
                ThreadId = Thread.CurrentThread.ManagedThreadId
            };

            // Добавляем в очередь (неблокирующая операция)
            _logQueue.Add(entry);
        }

        // Удобные методы-обертки
        public void Debug(string message, string source = null) => Log(LogLevel.Debug, message, source);
        public void Info(string message, string source = null) => Log(LogLevel.Info, message, source);
        public void Warning(string message, string source = null) => Log(LogLevel.Warning, message, source);
        public void Error(string message, string source = null) => Log(LogLevel.Error, message, source);
        public void Critical(string message, string source = null) => Log(LogLevel.Critical, message, source);

        /// <summary>
        /// Обработка очереди логов (работает в отдельном потоке)
        /// </summary>
        private async Task ProcessLogQueue() {
            var currentDate = DateTime.Now.Date;
            var currentLogFile = GetLogFilePath(currentDate);

            foreach (var entry in _logQueue.GetConsumingEnumerable(_cts.Token)) {
                try {
                    // Проверяем, не наступил ли новый день
                    if (entry.Timestamp.Date != currentDate) {
                        currentDate = entry.Timestamp.Date;
                        currentLogFile = GetLogFilePath(currentDate);
                    }

                    // Форматируем запись
                    var logLine = FormatLogEntry(entry);

                    // Пишем в файл
                    await File.AppendAllTextAsync(currentLogFile, logLine + Environment.NewLine);

                    // Выводим в консоль в Debug режиме
#if DEBUG
                    Console.WriteLine(logLine);
#endif

                    // Уведомляем подписчиков (например, UI)
                    OnLogWritten?.Invoke(logLine, entry.Level);
                } catch (Exception ex) {
                    // Не можем залогировать ошибку - пишем в консоль
                    Console.WriteLine($"Logger error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Форматирование записи лога
        /// </summary>
        private string FormatLogEntry(LogEntry entry) {
            return $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] " +
                   $"[{entry.Level.ToString().ToUpper()}] " +
                   $"[Thread {entry.ThreadId}] " +
                   $"[{entry.Source}] " +
                   $"{entry.Message}";
        }

        /// <summary>
        /// Получение информации о вызывающем методе
        /// </summary>
        private string GetCallerInfo() {
            try {
                var stackTrace = new System.Diagnostics.StackTrace();
                var frame = stackTrace.GetFrame(3); // Пропускаем Logger методы
                var method = frame?.GetMethod();
                var type = method?.DeclaringType;
                return type != null ? $"{type.Name}.{method?.Name}" : "Unknown";
            } catch {
                return "Unknown";
            }
        }

        /// <summary>
        /// Получение пути к файлу лога
        /// </summary>
        private string GetLogFilePath(DateTime date) {
            return Path.Combine(_logDirectory, $"vpn_{date:yyyyMMdd}.log");
        }

        /// <summary>
        /// Очистка старых логов (старше N дней)
        /// </summary>
        public void CleanupOldLogs(int daysToKeep = 30) {
            try {
                var cutoff = DateTime.Now.AddDays(-daysToKeep);
                var files = Directory.GetFiles(_logDirectory, "vpn_*.log");

                foreach (var file in files) {
                    var fileDate = ParseDateFromFilename(file);
                    if (fileDate < cutoff) {
                        File.Delete(file);
                        Info($"Deleted old log: {Path.GetFileName(file)}");
                    }
                }
            } catch (Exception ex) {
                Error($"Failed to cleanup old logs: {ex.Message}");
            }
        }

        private DateTime ParseDateFromFilename(string filename) {
            var name = Path.GetFileNameWithoutExtension(filename);
            var datePart = name.Replace("vpn_", "");
            return DateTime.ParseExact(datePart, "yyyyMMdd", null);
        }

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
            public string Message { get; set; }
            public string Source { get; set; }
            public int ThreadId { get; set; }
        }
    }
}