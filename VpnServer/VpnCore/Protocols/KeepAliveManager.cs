using VpnCore.Models;
using VpnCore.Networking;
using VpnCore.Utils;

namespace VpnCore.Protocols {
    /// <summary>
    /// Менеджер Keep-Alive сообщений
    /// Поддерживает соединение активным через NAT и файрволы
    /// Отслеживает состояние соединения и обнаруживает разрывы
    /// </summary>
    public sealed class KeepAliveManager : IDisposable {
        private readonly UdpTunnel _tunnel;
        private readonly Logger _logger;
        private Timer _keepAliveTimer;
        private Timer _healthCheckTimer;
        private CancellationTokenSource _cts;

        private int _missedHeartbeats;
        private DateTime _lastHeartbeatReceived;

        // Настройки
        private const int KeepAliveIntervalMs = 5000;   // Отправка каждые 5 секунд
        private const int HealthCheckIntervalMs = 1000; // Проверка каждую секунду
        private const int MaxMissedHeartbeats = 3;      // 3 пропущенных = соединение мертво

        public event Action OnConnectionAlive;
        public event Action OnConnectionDead;
        public event Action<long> OnPingReceived; // RTT в миллисекундах

        public bool IsAlive { get; private set; } = true;

        public KeepAliveManager(UdpTunnel tunnel) {
            _tunnel = tunnel;
            _logger = Logger.Instance;
            _cts = new CancellationTokenSource();
            _lastHeartbeatReceived = DateTime.UtcNow;
        }

        /// <summary>
        /// Запуск Keep-Alive механизма
        /// </summary>
        public void Start() {
            _keepAliveTimer = new Timer(SendKeepAlive, null, 0, KeepAliveIntervalMs);
            _healthCheckTimer = new Timer(CheckHealth, null, HealthCheckIntervalMs, HealthCheckIntervalMs);

            // Подписываемся на получение пакетов
            _tunnel.OnPacketReceived += HandlePacket;

            _logger.Info("KeepAlive manager started");
        }

        /// <summary>
        /// Отправка Keep-Alive пакета
        /// </summary>
        private async void SendKeepAlive(object state) {
            try {
                var packet = new VpnPacket(PacketType.KeepAlive, Array.Empty<byte>());
                await _tunnel.SendPacketAsync(packet);
                _logger.Debug("KeepAlive sent");
            } catch (Exception ex) {
                _logger.Error($"Failed to send KeepAlive: {ex.Message}");
            }
        }

        /// <summary>
        /// Отправка Ping для измерения RTT
        /// </summary>
        public async Task<long> SendPingAsync() {
            var startTime = DateTime.UtcNow;

            var pingPacket = new VpnPacket(PacketType.Ping, BitConverter.GetBytes(startTime.Ticks));
            await _tunnel.SendPacketAsync(pingPacket);

            // Ждем ответ
            var tcs = new TaskCompletionSource<long>();
            void Handler(byte[] data, System.Net.IPEndPoint endpoint) {
                var packet = VpnPacket.Deserialize(data);
                if (packet.Type == PacketType.Pong) {
                    var sentTicks = BitConverter.ToInt64(packet.Data, 0);
                    var rtt = (DateTime.UtcNow.Ticks - sentTicks) / TimeSpan.TicksPerMillisecond;
                    tcs.TrySetResult(rtt);
                }
            }

            _tunnel.OnPacketReceived += Handler;

            var timeoutTask = Task.Delay(5000);
            var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

            _tunnel.OnPacketReceived -= Handler;

            if (completedTask == timeoutTask)
                return -1; // Timeout

            return await tcs.Task;
        }

        /// <summary>
        /// Обработка входящих пакетов
        /// </summary>
        private void HandlePacket(byte[] data, System.Net.IPEndPoint endpoint) {
            try {
                var packet = VpnPacket.Deserialize(data);

                if (packet.Type == PacketType.KeepAlive) {
                    _lastHeartbeatReceived = DateTime.UtcNow;
                    _missedHeartbeats = 0;

                    if (!IsAlive) {
                        IsAlive = true;
                        OnConnectionAlive?.Invoke();
                        _logger.Info("Connection restored");
                    }
                } else if (packet.Type == PacketType.Ping) {
                    // Ответ на Ping
                    var pongPacket = new VpnPacket(PacketType.Pong, packet.Data);
                    _tunnel.SendPacketAsync(pongPacket).Wait(100);
                } else if (packet.Type == PacketType.Pong) {
                    var sentTicks = BitConverter.ToInt64(packet.Data, 0);
                    var rtt = (DateTime.UtcNow.Ticks - sentTicks) / TimeSpan.TicksPerMillisecond;
                    OnPingReceived?.Invoke(rtt);
                    _logger.Debug($"Pong received, RTT: {rtt}ms");
                }
            } catch (Exception ex) {
                _logger.Error($"KeepAlive packet processing error: {ex.Message}");
            }
        }

        /// <summary>
        /// Проверка здоровья соединения
        /// </summary>
        private void CheckHealth(object state) {
            var timeSinceLastHeartbeat = DateTime.UtcNow - _lastHeartbeatReceived;

            if (timeSinceLastHeartbeat.TotalMilliseconds > KeepAliveIntervalMs) {
                _missedHeartbeats++;

                if (_missedHeartbeats >= MaxMissedHeartbeats && IsAlive) {
                    IsAlive = false;
                    OnConnectionDead?.Invoke();
                    _logger.Warning("Connection dead - no heartbeat received");
                } else if (_missedHeartbeats > 0) {
                    _logger.Debug($"Missed heartbeat {_missedHeartbeats}/{MaxMissedHeartbeats}");
                }
            }
        }

        /// <summary>
        /// Принудительная отправка Keep-Alive
        /// </summary>
        public async Task ForceKeepAliveAsync() {
            await SendPingAsync();
        }

        /// <summary>
        /// Сброс счетчиков (при переподключении)
        /// </summary>
        public void Reset() {
            _missedHeartbeats = 0;
            _lastHeartbeatReceived = DateTime.UtcNow;
            IsAlive = true;
            _logger.Debug("KeepAlive manager reset");
        }

        public void Dispose() {
            _keepAliveTimer?.Dispose();
            _healthCheckTimer?.Dispose();
            _cts?.Cancel();
            _cts?.Dispose();
            _tunnel.OnPacketReceived -= HandlePacket;
        }
    }
}