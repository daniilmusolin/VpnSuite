using System.Collections.Concurrent;

namespace VpnCore.Utils {
    /// <summary>
    /// Сборщик метрик производительности
    /// Собирает статистику в реальном времени: скорость, задержки, потери
    /// Используется для мониторинга и отображения в UI
    /// </summary>
    public sealed class MetricsCollector : IDisposable {
        private static MetricsCollector _instance;
        private static readonly object _lock = new object();

        // Счетчики
        private long _bytesSent;
        private long _bytesReceived;
        private long _packetsSent;
        private long _packetsReceived;
        private long _packetsLost;

        // Скользящие окна для расчета скорости
        private readonly RollingWindow _sendWindow;
        private readonly RollingWindow _receiveWindow;

        // История для графиков
        private readonly ConcurrentQueue<MetricSample> _history;
        private readonly int _maxHistorySize = 300; // 5 минут при 1 секунде интервала

        private Timer _updateTimer;
        private readonly Logger _logger;
        private bool _disposed;

        // События
        public event Action<double, double> OnSpeedUpdated; // download, upload
        public event Action<int, double> OnPacketStatsUpdated; // loss %, rtt

        public static MetricsCollector Instance {
            get {
                if (_instance == null) {
                    lock (_lock) {
                        _instance ??= new MetricsCollector();
                    }
                }
                return _instance;
            }
        }

        private MetricsCollector() {
            _logger = Logger.Instance;
            _sendWindow = new RollingWindow(TimeSpan.FromSeconds(10));
            _receiveWindow = new RollingWindow(TimeSpan.FromSeconds(10));
            _history = new ConcurrentQueue<MetricSample>();

            _updateTimer = new Timer(UpdateMetrics, null, 1000, 1000);
        }

        /// <summary>
        /// Запись отправленных данных
        /// </summary>
        public void RecordSend(int bytes) {
            Interlocked.Add(ref _bytesSent, bytes);
            Interlocked.Increment(ref _packetsSent);
            _sendWindow.Add(bytes);
        }

        /// <summary>
        /// Запись полученных данных
        /// </summary>
        public void RecordReceive(int bytes) {
            Interlocked.Add(ref _bytesReceived, bytes);
            Interlocked.Increment(ref _packetsReceived);
            _receiveWindow.Add(bytes);
        }

        /// <summary>
        /// Запись потерянного пакета
        /// </summary>
        public void RecordLoss() {
            Interlocked.Increment(ref _packetsLost);
        }

        /// <summary>
        /// Текущая скорость отправки (байт/сек)
        /// </summary>
        public double GetCurrentSendSpeed() => _sendWindow.GetRate();

        /// <summary>
        /// Текущая скорость получения (байт/сек)
        /// </summary>
        public double GetCurrentReceiveSpeed() => _receiveWindow.GetRate();

        /// <summary>
        /// Общее количество отправленных байт
        /// </summary>
        public long TotalBytesSent => Interlocked.Read(ref _bytesSent);

        /// <summary>
        /// Общее количество полученных байт
        /// </summary>
        public long TotalBytesReceived => Interlocked.Read(ref _bytesReceived);

        /// <summary>
        /// Процент потери пакетов
        /// </summary>
        public double GetPacketLossPercentage() {
            var total = _packetsSent + _packetsReceived;
            if (total == 0) return 0;
            return (double)_packetsLost / total * 100;
        }

        /// <summary>
        /// Получение полной сводки метрик
        /// </summary>
        public MetricsSummary GetSummary() {
            return new MetricsSummary {
                BytesSent = TotalBytesSent,
                BytesReceived = TotalBytesReceived,
                PacketsSent = _packetsSent,
                PacketsReceived = _packetsReceived,
                PacketsLost = _packetsLost,
                CurrentSendSpeed = GetCurrentSendSpeed(),
                CurrentReceiveSpeed = GetCurrentReceiveSpeed(),
                PacketLossPercentage = GetPacketLossPercentage(),
                SendWindowRate = _sendWindow.GetRate(),
                ReceiveWindowRate = _receiveWindow.GetRate()
            };
        }

        /// <summary>
        /// Получение истории метрик для графиков
        /// </summary>
        public List<MetricSample> GetHistory(int count) {
            return _history.TakeLast(Math.Min(count, _history.Count)).ToList();
        }

        /// <summary>
        /// Периодическое обновление метрик
        /// </summary>
        private void UpdateMetrics(object state) {
            var sendSpeed = GetCurrentSendSpeed();
            var receiveSpeed = GetCurrentReceiveSpeed();
            var packetLoss = GetPacketLossPercentage();

            // Сохраняем в историю
            var sample = new MetricSample {
                Timestamp = DateTime.UtcNow,
                SendSpeed = sendSpeed,
                ReceiveSpeed = receiveSpeed,
                PacketLoss = packetLoss
            };

            _history.Enqueue(sample);

            // Ограничиваем размер истории
            while (_history.Count > _maxHistorySize)
                _history.TryDequeue(out _);

            // Уведомляем подписчиков
            OnSpeedUpdated?.Invoke(receiveSpeed, sendSpeed);
            OnPacketStatsUpdated?.Invoke((int)packetLoss, 0);
        }

        /// <summary>
        /// Сброс всех счетчиков
        /// </summary>
        public void Reset() {
            Interlocked.Exchange(ref _bytesSent, 0);
            Interlocked.Exchange(ref _bytesReceived, 0);
            Interlocked.Exchange(ref _packetsSent, 0);
            Interlocked.Exchange(ref _packetsReceived, 0);
            Interlocked.Exchange(ref _packetsLost, 0);

            _sendWindow.Reset();
            _receiveWindow.Reset();
            _history.Clear();

            _logger.Info("Metrics collector reset");
        }

        public void Dispose() {
            if (!_disposed) {
                _updateTimer?.Dispose();
                _disposed = true;
            }
        }

        /// <summary>
        /// Скользящее окно для расчета скорости
        /// </summary>
        private class RollingWindow {
            private readonly ConcurrentQueue<(DateTime Time, int Bytes)> _samples;
            private readonly TimeSpan _windowSize;
            private long _totalBytes;

            public RollingWindow(TimeSpan windowSize) {
                _windowSize = windowSize;
                _samples = new ConcurrentQueue<(DateTime, int)>();
            }

            public void Add(int bytes) {
                var now = DateTime.UtcNow;
                _samples.Enqueue((now, bytes));
                Interlocked.Add(ref _totalBytes, bytes);
                Cleanup(now);
            }

            public double GetRate() {
                var now = DateTime.UtcNow;
                Cleanup(now);

                var oldest = now - _windowSize;
                var bytes = _samples.Where(s => s.Time > oldest).Sum(s => s.Bytes);
                return bytes / _windowSize.TotalSeconds;
            }

            private void Cleanup(DateTime now) {
                var cutoff = now - _windowSize;
                while (_samples.TryPeek(out var sample) && sample.Time < cutoff) {
                    if (_samples.TryDequeue(out sample)) {
                        Interlocked.Add(ref _totalBytes, -sample.Bytes);
                    }
                }
            }

            public void Reset() {
                while (_samples.TryDequeue(out _)) { }
                Interlocked.Exchange(ref _totalBytes, 0);
            }
        }
    }

    public class MetricsSummary {
        public long BytesSent { get; set; }
        public long BytesReceived { get; set; }
        public long PacketsSent { get; set; }
        public long PacketsReceived { get; set; }
        public long PacketsLost { get; set; }
        public double CurrentSendSpeed { get; set; }
        public double CurrentReceiveSpeed { get; set; }
        public double PacketLossPercentage { get; set; }
        public double SendWindowRate { get; set; }
        public double ReceiveWindowRate { get; set; }

        public string GetFormattedSendSpeed() => FormatSpeed(CurrentSendSpeed);
        public string GetFormattedReceiveSpeed() => FormatSpeed(CurrentReceiveSpeed);

        private string FormatSpeed(double bytesPerSecond) {
            if (bytesPerSecond >= 1024 * 1024)
                return $"{bytesPerSecond / (1024 * 1024):F1} MB/s";
            if (bytesPerSecond >= 1024)
                return $"{bytesPerSecond / 1024:F1} KB/s";
            return $"{bytesPerSecond:F0} B/s";
        }
    }

    public class MetricSample {
        public DateTime Timestamp { get; set; }
        public double SendSpeed { get; set; }
        public double ReceiveSpeed { get; set; }
        public double PacketLoss { get; set; }
    }
}