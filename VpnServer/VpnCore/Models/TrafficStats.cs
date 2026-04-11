using System.Collections.Concurrent;

namespace VpnCore.Models {
    /// <summary>
    /// Сбор и анализ статистики трафика в реальном времени
    /// Использует lock-free структуры для максимальной производительности
    /// </summary>
    public sealed class TrafficStats {
        // === Базовые счетчики (используют атомарные операции для потокобезопасности) ===
        private long _totalBytesSent;        // Всего отправлено байт
        private long _totalBytesReceived;    // Всего получено байт
        private long _totalPacketsSent;      // Всего отправлено пакетов
        private long _totalPacketsReceived;  // Всего получено пакетов
        private long _totalPacketsLost;      // Всего потеряно пакетов
        private long _totalPacketsRetransmitted; // Всего переотправлено пакетов

        // === Очереди сэмплов для расчета скорости ===
        // ConcurrentQueue - потокобезопасная очередь без блокировок
        private readonly ConcurrentQueue<TrafficSample> _sendSamples;    // Сэмплы отправленных данных
        private readonly ConcurrentQueue<TrafficSample> _receiveSamples; // Сэмплы полученных данных

        // === Статистика по отдельным потокам (мультиплексирование) ===
        // ConcurrentDictionary - потокобезопасная коллекция ключ-значение
        private readonly ConcurrentDictionary<string, StreamStats> _streamStats;

        private readonly int _maxSamples = 60; // Максимальное количество сэмплов для расчета скорости (60 секунд)

        public TrafficStats() {
            _sendSamples = new ConcurrentQueue<TrafficSample>();
            _receiveSamples = new ConcurrentQueue<TrafficSample>();
            _streamStats = new ConcurrentDictionary<string, StreamStats>();
        }

        /// <summary>
        /// Запись отправленных данных
        /// Использует Interlocked для атомарного обновления счетчиков без блокировок
        /// </summary>
        /// <param name="bytes">Количество отправленных байт</param>
        public void RecordSend(int bytes) {
            // Interlocked.Add - атомарно добавляет значение к переменной
            // Не требует блокировок, работает быстрее lock
            Interlocked.Add(ref _totalBytesSent, bytes);
            Interlocked.Increment(ref _totalPacketsSent);

            // Сохраняем сэмпл для расчета скорости
            var sample = new TrafficSample(DateTime.UtcNow, bytes);
            _sendSamples.Enqueue(sample);
            TrimSamples(_sendSamples);
        }

        /// <summary>
        /// Запись полученных данных
        /// </summary>
        public void RecordReceive(int bytes) {
            Interlocked.Add(ref _totalBytesReceived, bytes);
            Interlocked.Increment(ref _totalPacketsReceived);

            var sample = new TrafficSample(DateTime.UtcNow, bytes);
            _receiveSamples.Enqueue(sample);
            TrimSamples(_receiveSamples);
        }

        /// <summary>
        /// Запись потерянного пакета (используется для расчета качества соединения)
        /// </summary>
        public void RecordLoss() {
            Interlocked.Increment(ref _totalPacketsLost);
        }

        /// <summary>
        /// Запись переотправленного пакета (показатель надежности соединения)
        /// </summary>
        public void RecordRetransmit() {
            Interlocked.Increment(ref _totalPacketsRetransmitted);
        }

        /// <summary>
        /// Статистика по отдельному потоку (для мультиплексирования)
        /// </summary>
        /// <param name="streamId">Идентификатор потока</param>
        /// <param name="bytes">Байты</param>
        /// <param name="isUpload">true - отправка, false - получение</param>
        public void RecordStreamStats(string streamId, int bytes, bool isUpload) {
            // GetOrAdd - атомарно получает или создает запись в словаре
            var stats = _streamStats.GetOrAdd(streamId, _ => new StreamStats());

            if (isUpload)
                stats.UploadBytes += bytes;  // Суммирование не атомарно, но для статистики допустимо
            else
                stats.DownloadBytes += bytes;

            stats.LastActivity = DateTime.UtcNow;
        }

        /// <summary>
        /// Обрезка старых сэмплов, чтобы очередь не разрасталась бесконечно
        /// </summary>
        private void TrimSamples(ConcurrentQueue<TrafficSample> queue) {
            // Удаляем лишние элементы, пока размер не станет <= _maxSamples
            while (queue.Count > _maxSamples)
                queue.TryDequeue(out _); // out _ - игнорируем удаленный элемент
        }

        /// <summary>
        /// Расчет текущей скорости отправки за указанный интервал
        /// </summary>
        /// <param name="interval">Интервал времени (обычно 1 секунда)</param>
        /// <returns>Скорость в байтах/секунду</returns>
        public double GetCurrentSendSpeed(TimeSpan interval) {
            var cutoff = DateTime.UtcNow - interval; // Временная граница

            // LINQ: фильтруем сэмплы новее cutoff и суммируем байты
            var bytes = _sendSamples.Where(s => s.Timestamp > cutoff).Sum(s => s.Bytes);

            // Скорость = байты / время
            return bytes / interval.TotalSeconds;
        }

        /// <summary>
        /// Расчет текущей скорости получения
        /// </summary>
        public double GetCurrentReceiveSpeed(TimeSpan interval) {
            var cutoff = DateTime.UtcNow - interval;
            var bytes = _receiveSamples.Where(s => s.Timestamp > cutoff).Sum(s => s.Bytes);
            return bytes / interval.TotalSeconds;
        }

        /// <summary>
        /// Средняя скорость отправки за все время
        /// </summary>
        public double GetAverageSendSpeed() {
            if (!_sendSamples.Any()) return 0;
            // Average - среднее арифметическое
            return _sendSamples.Average(s => s.Bytes) / 1.0;
        }

        /// <summary>
        /// Средняя скорость получения за все время
        /// </summary>
        public double GetAverageReceiveSpeed() {
            if (!_receiveSamples.Any()) return 0;
            return _receiveSamples.Average(s => s.Bytes) / 1.0;
        }

        /// <summary>
        /// Процент потери пакетов (важный показатель качества VPN)
        /// </summary>
        public double GetPacketLossPercentage() {
            var total = _totalPacketsSent + _totalPacketsReceived;
            if (total == 0) return 0;

            // (Потеряно / Всего) * 100%
            return (double)_totalPacketsLost / total * 100;
        }

        /// <summary>
        /// Получение полной сводки статистики
        /// </summary>
        public TrafficSummary GetSummary() {
            return new TrafficSummary {
                TotalBytesSent = _totalBytesSent,
                TotalBytesReceived = _totalBytesReceived,
                TotalPacketsSent = _totalPacketsSent,
                TotalPacketsReceived = _totalPacketsReceived,
                TotalPacketsLost = _totalPacketsLost,
                TotalPacketsRetransmitted = _totalPacketsRetransmitted,
                CurrentSendSpeed = GetCurrentSendSpeed(TimeSpan.FromSeconds(1)),
                CurrentReceiveSpeed = GetCurrentReceiveSpeed(TimeSpan.FromSeconds(1)),
                AverageSendSpeed = GetAverageSendSpeed(),
                AverageReceiveSpeed = GetAverageReceiveSpeed(),
                PacketLossPercentage = GetPacketLossPercentage(),
                Streams = _streamStats.ToDictionary(x => x.Key, x => x.Value)
            };
        }

        /// <summary>
        /// Сброс всей статистики (при переподключении)
        /// </summary>
        public void Reset() {
            // Interlocked.Exchange - атомарно заменяет значение и возвращает старое
            Interlocked.Exchange(ref _totalBytesSent, 0);
            Interlocked.Exchange(ref _totalBytesReceived, 0);
            Interlocked.Exchange(ref _totalPacketsSent, 0);
            Interlocked.Exchange(ref _totalPacketsReceived, 0);
            Interlocked.Exchange(ref _totalPacketsLost, 0);
            Interlocked.Exchange(ref _totalPacketsRetransmitted, 0);

            // Очистка коллекций
            _sendSamples.Clear();
            _receiveSamples.Clear();
            _streamStats.Clear();
        }

        /// <summary>
        /// Внутренний класс для хранения одного измерения трафика
        /// </summary>
        private class TrafficSample {
            public DateTime Timestamp { get; } // Когда произошло измерение
            public int Bytes { get; }          // Сколько байт передано

            public TrafficSample(DateTime timestamp, int bytes) {
                Timestamp = timestamp;
                Bytes = bytes;
            }
        }
    }

    /// <summary>
    /// Статистика по одному потоку (для мультиплексирования)
    /// </summary>
    public class StreamStats {
        public long UploadBytes { get; set; }   // Отправлено байт в этом потоке
        public long DownloadBytes { get; set; } // Получено байт в этом потоке
        public DateTime LastActivity { get; set; } // Последняя активность
        public DateTime CreatedAt { get; } = DateTime.UtcNow; // Время создания потока

        public long TotalBytes => UploadBytes + DownloadBytes; // Всего байт в потоке
    }

    /// <summary>
    /// Сводка статистики для отображения в UI
    /// </summary>
    public class TrafficSummary {
        // === Сырые данные ===
        public long TotalBytesSent { get; set; }
        public long TotalBytesReceived { get; set; }
        public long TotalPacketsSent { get; set; }
        public long TotalPacketsReceived { get; set; }
        public long TotalPacketsLost { get; set; }
        public long TotalPacketsRetransmitted { get; set; }

        // === Скорости ===
        public double CurrentSendSpeed { get; set; }     // Текущая скорость отправки (байт/сек)
        public double CurrentReceiveSpeed { get; set; }  // Текущая скорость получения (байт/сек)
        public double AverageSendSpeed { get; set; }     // Средняя скорость отправки
        public double AverageReceiveSpeed { get; set; }  // Средняя скорость получения

        // === Качество ===
        public double PacketLossPercentage { get; set; } // Процент потери пакетов

        // === Детализация по потокам ===
        public Dictionary<string, StreamStats> Streams { get; set; }

        // === Форматирование для UI ===

        /// <summary>
        /// Форматирует скорость отправки в человеко-читаемый вид
        /// </summary>
        public string GetFormattedSendSpeed() {
            return FormatSpeed(CurrentSendSpeed);
        }

        /// <summary>
        /// Форматирует скорость получения в человеко-читаемый вид
        /// </summary>
        public string GetFormattedReceiveSpeed() {
            return FormatSpeed(CurrentReceiveSpeed);
        }

        /// <summary>
        /// Преобразует байты/сек в KB/s или MB/s
        /// </summary>
        private string FormatSpeed(double bytesPerSecond) {
            if (bytesPerSecond >= 1024 * 1024)  // >= 1 MB/s
                return $"{bytesPerSecond / (1024 * 1024):F1} MB/s";
            if (bytesPerSecond >= 1024)          // >= 1 KB/s
                return $"{bytesPerSecond / 1024:F1} KB/s";
            return $"{bytesPerSecond:F0} B/s";
        }

        /// <summary>
        /// Форматирует общее количество отправленных данных
        /// </summary>
        public string GetFormattedTotalSend() {
            return FormatBytes(TotalBytesSent);
        }

        /// <summary>
        /// Форматирует общее количество полученных данных
        /// </summary>
        public string GetFormattedTotalReceive() {
            return FormatBytes(TotalBytesReceived);
        }

        /// <summary>
        /// Преобразует байты в KB, MB или GB
        /// </summary>
        private string FormatBytes(long bytes) {
            if (bytes >= 1024 * 1024 * 1024)  // >= 1 GB
                return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
            if (bytes >= 1024 * 1024)          // >= 1 MB
                return $"{bytes / (1024.0 * 1024):F1} MB";
            if (bytes >= 1024)                 // >= 1 KB
                return $"{bytes / 1024.0:F1} KB";
            return $"{bytes} B";
        }

        /// <summary>
        /// Строковое представление для логов
        /// </summary>
        public override string ToString() {
            return $"↓ {GetFormattedReceiveSpeed()} ↑ {GetFormattedSendSpeed()} | " +
                   $"Total: ↓ {GetFormattedTotalReceive()} ↑ {GetFormattedTotalSend()} | " +
                   $"Loss: {PacketLossPercentage:F1}%";
        }
    }
}