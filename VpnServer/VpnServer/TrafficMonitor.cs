using System.Collections.Concurrent;
using VpnCore.Utils;

namespace VpnServer {
    /// <summary>
    /// Мониторинг трафика сервера
    /// Собирает и отображает статистику по всем клиентам
    /// </summary>
    public sealed class TrafficMonitor : IDisposable {
        private readonly ConcurrentDictionary<string, ClientTrafficStats> _trafficStats;
        private readonly Logger _logger;
        private Timer _displayTimer;
        private Timer _resetTimer;
        private bool _disposed;

        public event Action<TrafficReport> OnReportReady;

        public TrafficMonitor() {
            _trafficStats = new ConcurrentDictionary<string, ClientTrafficStats>();
            _logger = Logger.Instance;
        }

        public void Start() {
            // Отображение статистики каждые 10 секунд
            _displayTimer = new Timer(DisplayStatistics, null, 10000, 10000);

            // Сброс счетчиков скорости каждую секунду
            _resetTimer = new Timer(ResetSpeedCounters, null, 1000, 1000);

            _logger.Info("Traffic monitor started");
        }

        /// <summary>
        /// Регистрация отправленных данных
        /// </summary>
        public void RecordSend(string clientId, int bytes) {
            var stats = _trafficStats.GetOrAdd(clientId, _ => new ClientTrafficStats());
            stats.TotalBytesSent += bytes;
            stats.CurrentSendSpeed += bytes;
        }

        /// <summary>
        /// Регистрация полученных данных
        /// </summary>
        public void RecordReceive(string clientId, int bytes) {
            var stats = _trafficStats.GetOrAdd(clientId, _ => new ClientTrafficStats());
            stats.TotalBytesReceived += bytes;
            stats.CurrentReceiveSpeed += bytes;
        }

        /// <summary>
        /// Сброс счетчиков скорости
        /// </summary>
        private void ResetSpeedCounters(object state) {
            foreach (var stats in _trafficStats.Values) {
                stats.SendSpeedHistory.Add(stats.CurrentSendSpeed);
                stats.ReceiveSpeedHistory.Add(stats.CurrentReceiveSpeed);

                // Ограничиваем историю
                while (stats.SendSpeedHistory.Count > 60)
                    stats.SendSpeedHistory.RemoveAt(0);
                while (stats.ReceiveSpeedHistory.Count > 60)
                    stats.ReceiveSpeedHistory.RemoveAt(0);

                stats.CurrentSendSpeed = 0;
                stats.CurrentReceiveSpeed = 0;
            }
        }

        /// <summary>
        /// Отображение статистики
        /// </summary>
        private void DisplayStatistics(object state) {
            if (!_trafficStats.Any())
                return;

            Console.Clear();
            Console.WriteLine($@"
                ╔══════════════════════════════════════════════════════════════════════════════╗
                ║                           VPN SERVER TRAFFIC STATISTICS                       ║
                ╠══════════════════════════════════════════════════════════════════════════════╣
                ║ Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}                                                      ║
                ║ Active Clients: {_trafficStats.Count}                                                          ║
                ╠══════════════════════════════════════════════════════════════════════════════╣
                ");

            Console.WriteLine(@" ║ Client ID     │ Download (KB/s) │ Upload (KB/s) │ Total Down (MB) │ Total Up (MB) ║");
            Console.WriteLine(@" ╠═══════════════╪═════════════════╪════════════════╪═════════════════╪═══════════════╣");

            foreach (var kvp in _trafficStats.OrderByDescending(x => x.Value.TotalBytesSent + x.Value.TotalBytesReceived)) {
                var stats = kvp.Value;
                var clientId = kvp.Key.PadRight(13);
                var downSpeed = (stats.CurrentReceiveSpeed / 1024.0).ToString("F1").PadLeft(15);
                var upSpeed = (stats.CurrentSendSpeed / 1024.0).ToString("F1").PadLeft(14);
                var totalDown = (stats.TotalBytesReceived / (1024.0 * 1024)).ToString("F1").PadLeft(15);
                var totalUp = (stats.TotalBytesSent / (1024.0 * 1024)).ToString("F1").PadLeft(14);

                Console.WriteLine($" ║ {clientId} │ {downSpeed} KB/s │ {upSpeed} KB/s │ {totalDown} MB │ {totalUp} MB ║");
            }

            // Общая статистика
            var totalSent = _trafficStats.Values.Sum(s => s.TotalBytesSent);
            var totalReceived = _trafficStats.Values.Sum(s => s.TotalBytesReceived);
            var totalSpeedSent = _trafficStats.Values.Sum(s => s.CurrentSendSpeed);
            var totalSpeedReceived = _trafficStats.Values.Sum(s => s.CurrentReceiveSpeed);

            Console.WriteLine($@"
                ╠══════════════════════════════════════════════════════════════════════════════╣
                ║ TOTAL          │ {(totalSpeedReceived / 1024.0):F1} KB/s │ {(totalSpeedSent / 1024.0):F1} KB/s │ {(totalReceived / (1024.0 * 1024)):F1} MB │ {(totalSent / (1024.0 * 1024)):F1} MB ║
                ╚══════════════════════════════════════════════════════════════════════════════╝
                ");

            // Отправляем отчет
            var report = new TrafficReport {
                Timestamp = DateTime.Now,
                ActiveClients = _trafficStats.Count,
                TotalBytesSent = totalSent,
                TotalBytesReceived = totalReceived,
                CurrentSendSpeed = totalSpeedSent,
                CurrentReceiveSpeed = totalSpeedReceived,
                Clients = _trafficStats.ToDictionary(x => x.Key, x => x.Value)
            };

            OnReportReady?.Invoke(report);
        }

        /// <summary>
        /// Удаление клиента из мониторинга
        /// </summary>
        public void RemoveClient(string clientId) {
            _trafficStats.TryRemove(clientId, out _);
        }

        /// <summary>
        /// Получение статистики клиента
        /// </summary>
        public ClientTrafficStats GetClientStats(string clientId) {
            return _trafficStats.GetOrAdd(clientId, _ => new ClientTrafficStats());
        }

        public void Dispose() {
            if (!_disposed) {
                _displayTimer?.Dispose();
                _resetTimer?.Dispose();
                _trafficStats.Clear();
                _disposed = true;
            }
        }
    }

    public class ClientTrafficStats {
        public long TotalBytesSent { get; set; }
        public long TotalBytesReceived { get; set; }
        public long CurrentSendSpeed { get; set; }
        public long CurrentReceiveSpeed { get; set; }
        public List<long> SendSpeedHistory { get; } = new List<long>();
        public List<long> ReceiveSpeedHistory { get; } = new List<long>();

        public double AverageSendSpeed => SendSpeedHistory.DefaultIfEmpty().Average();
        public double AverageReceiveSpeed => ReceiveSpeedHistory.DefaultIfEmpty().Average();
        public long TotalBytes => TotalBytesSent + TotalBytesReceived;
    }

    public class TrafficReport {
        public DateTime Timestamp { get; set; }
        public int ActiveClients { get; set; }
        public long TotalBytesSent { get; set; }
        public long TotalBytesReceived { get; set; }
        public long CurrentSendSpeed { get; set; }
        public long CurrentReceiveSpeed { get; set; }
        public Dictionary<string, ClientTrafficStats> Clients { get; set; }
    }
}