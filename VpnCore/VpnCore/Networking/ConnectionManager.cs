using System.Collections.Concurrent;
using System.Net;
using VpnCore.Models;
using VpnCore.Utils;

namespace VpnCore.Networking {
    /// <summary>
    /// Менеджер соединений
    /// Управляет множеством VPN соединений, отслеживает их состояние
    /// </summary>
    public sealed class ConnectionManager : IDisposable {
        private readonly ConcurrentDictionary<Guid, ConnectionInfo> _connections;
        private readonly Timer _healthCheckTimer;
        private readonly Logger _logger;
        private bool _disposed;

        public event Action<ConnectionInfo> OnConnectionAdded;
        public event Action<ConnectionInfo> OnConnectionRemoved;
        public event Action<ConnectionInfo> OnConnectionStateChanged;

        public int ActiveConnections => _connections.Count;

        public ConnectionManager() {
            _connections = new ConcurrentDictionary<Guid, ConnectionInfo>();
            _logger = Logger.Instance;

            // Проверка здоровья каждые 30 секунд
            _healthCheckTimer = new Timer(HealthCheck, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        /// <summary>
        /// Добавление нового соединения
        /// </summary>
        public ConnectionInfo AddConnection(string name, IPAddress remoteAddress, int remotePort, ConnectionRole role = ConnectionRole.Client) {
            var connection = new ConnectionInfo {
                Name = name,
                RemoteAddress = remoteAddress,
                RemotePort = remotePort,
                Role = role,
                State = ConnectionState.Connecting
            };

            _connections[connection.ConnectionId] = connection;
            OnConnectionAdded?.Invoke(connection);
            _logger.Info($"Connection added: {connection.ConnectionId}");

            return connection;
        }

        /// <summary>
        /// Получение соединения по ID
        /// </summary>
        public ConnectionInfo GetConnection(Guid connectionId) {
            return _connections.TryGetValue(connectionId, out var connection) ? connection : null;
        }

        /// <summary>
        /// Обновление состояния соединения
        /// </summary>
        public void UpdateConnectionState(Guid connectionId, ConnectionState newState) {
            if (_connections.TryGetValue(connectionId, out var connection)) {
                connection.State = newState;

                if (newState == ConnectionState.Established)
                    connection.ConnectedAt = DateTime.UtcNow;

                OnConnectionStateChanged?.Invoke(connection);
                _logger.Debug($"Connection {connectionId} state: {newState}");
            }
        }

        /// <summary>
        /// Обновление метрик соединения
        /// </summary>
        public void UpdateConnectionMetrics(Guid connectionId, int bytesSent, int bytesReceived) {
            if (_connections.TryGetValue(connectionId, out var connection)) {
                connection.RecordPacketSent(bytesSent);
                connection.RecordPacketReceived(bytesReceived);
            }
        }

        /// <summary>
        /// Обновление RTT
        /// </summary>
        public void UpdateRtt(Guid connectionId, int newRtt) {
            if (_connections.TryGetValue(connectionId, out var connection)) {
                connection.UpdateRtt(newRtt);
            }
        }

        /// <summary>
        /// Удаление соединения
        /// </summary>
        public bool RemoveConnection(Guid connectionId) {
            if (_connections.TryRemove(connectionId, out var connection)) {
                connection.State = ConnectionState.Closed;
                OnConnectionRemoved?.Invoke(connection);
                _logger.Info($"Connection removed: {connectionId}");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Получение всех соединений
        /// </summary>
        public ConnectionInfo[] GetAllConnections() {
            return _connections.Values.ToArray();
        }

        /// <summary>
        /// Периодическая проверка здоровья соединений
        /// </summary>
        private void HealthCheck(object state) {
            var now = DateTime.UtcNow;

            foreach (var connection in _connections.Values) {
                // Проверка таймаута (30 секунд без активности)
                if (connection.State == ConnectionState.Established) {
                    var inactiveTime = now - connection.LastActivityAt;
                    if (inactiveTime > TimeSpan.FromSeconds(30)) {
                        _logger.Warning($"Connection {connection.ConnectionId} inactive for {inactiveTime.Seconds}s");
                        UpdateConnectionState(connection.ConnectionId, ConnectionState.Reconnecting);
                    }
                }

                // Обновление качества соединения
                connection.UpdateConnectionQuality();
            }
        }

        public void Dispose() {
            if (!_disposed) {
                _healthCheckTimer?.Dispose();
                _connections.Clear();
                _disposed = true;
            }
        }
    }
}