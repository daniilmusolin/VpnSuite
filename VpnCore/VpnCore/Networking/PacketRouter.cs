using System.Collections.Concurrent;
using VpnCore.Models;
using VpnCore.Utils;

namespace VpnCore.Networking {
    /// <summary>
    /// Маршрутизатор пакетов
    /// Определяет, куда направить каждый пакет на основе его типа
    /// </summary>
    public sealed class PacketRouter {
        private readonly ConcurrentDictionary<PacketType, Func<VpnPacket, Task>> _handlers;
        private readonly Logger _logger;

        public PacketRouter() {
            _handlers = new ConcurrentDictionary<PacketType, Func<VpnPacket, Task>>();
            _logger = Logger.Instance;
            RegisterDefaultHandlers();
        }

        /// <summary>
        /// Регистрация обработчика для определенного типа пакетов
        /// </summary>
        public void RegisterHandler(PacketType type, Func<VpnPacket, Task> handler) {
            _handlers[type] = handler;
            _logger.Debug($"Registered handler for {type}");
        }

        /// <summary>
        /// Удаление обработчика
        /// </summary>
        public void UnregisterHandler(PacketType type) {
            _handlers.TryRemove(type, out _);
        }

        /// <summary>
        /// Маршрутизация пакета к соответствующему обработчику
        /// </summary>
        public async Task RouteAsync(VpnPacket packet) {
            if (packet == null) return;

            if (_handlers.TryGetValue(packet.Type, out var handler)) {
                try {
                    await handler(packet);
                } catch (Exception ex) {
                    _logger.Error($"Handler for {packet.Type} failed: {ex.Message}");
                }
            } else {
                _logger.Warning($"No handler registered for {packet.Type}");
            }
        }

        /// <summary>
        /// Регистрация стандартных обработчиков
        /// </summary>
        private void RegisterDefaultHandlers() {
            RegisterHandler(PacketType.KeepAlive, HandleKeepAlive);
            RegisterHandler(PacketType.Ping, HandlePing);
            RegisterHandler(PacketType.Pong, HandlePong);
            RegisterHandler(PacketType.Disconnect, HandleDisconnect);
            RegisterHandler(PacketType.Error, HandleError);
        }

        private Task HandleKeepAlive(VpnPacket packet) {
            _logger.Debug($"KeepAlive received from {packet.PacketId}");
            return Task.CompletedTask;
        }

        private Task HandlePing(VpnPacket packet) {
            // Отправляем Pong ответ
            _logger.Debug($"Ping received, sending Pong");
            return Task.CompletedTask;
        }

        private Task HandlePong(VpnPacket packet) {
            var rtt = (uint)(DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond) - packet.Timestamp;
            _logger.Debug($"Pong received, RTT: {rtt}ms");
            return Task.CompletedTask;
        }

        private Task HandleDisconnect(VpnPacket packet) {
            _logger.Info("Disconnect signal received");
            return Task.CompletedTask;
        }

        private Task HandleError(VpnPacket packet) {
            var errorMsg = System.Text.Encoding.UTF8.GetString(packet.Data);
            _logger.Error($"Remote error: {errorMsg}");
            return Task.CompletedTask;
        }
    }
}