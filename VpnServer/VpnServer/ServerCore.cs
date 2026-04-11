using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using VpnCore.Cryptography;
using VpnCore.Models;
using VpnCore.Protocols;
using VpnCore.Utils;

namespace VpnServer {
    /// <summary>
    /// Ядро VPN сервера
    /// Управляет клиентами, обрабатывает рукопожатия и маршрутизирует трафик
    /// </summary>
    public sealed class ServerCore : IDisposable {
        private UdpClient _udpServer;
        private readonly ServerConfig _config;
        private readonly ConcurrentDictionary<string, ClientSession> _clients;
        private readonly ConcurrentDictionary<string, DateTime> _pendingHandshakes;
        private readonly Logger _logger;
        private CancellationTokenSource _cts;
        private Task _receiveTask;
        private int _nextClientId;
        private bool _isRunning;
        private bool _disposed;

        // События
        public event Action<string, string> OnClientConnected;
        public event Action<string, string> OnClientDisconnected;
        public event Action<string> OnError;

        public int ActiveClients => _clients.Count;
        public bool IsRunning => _isRunning;

        public ServerCore(ServerConfig config) {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _clients = new ConcurrentDictionary<string, ClientSession>();
            _pendingHandshakes = new ConcurrentDictionary<string, DateTime>();
            _logger = Logger.Instance;
            _config.Validate();
        }

        /// <summary>
        /// Запуск сервера
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken = default) {
            if (_isRunning)
                throw new InvalidOperationException("Server is already running");

            _logger.Info($"Starting server on {_config.ListenAddress}:{_config.ListenPort}");
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try {
                // Создаем UDP сервер
                var endPoint = new IPEndPoint(
                    IPAddress.Parse(_config.ListenAddress),
                    _config.ListenPort
                );

                _udpServer = new UdpClient(endPoint);
                _udpServer.Client.ReceiveBufferSize = 2 * 1024 * 1024;
                _udpServer.Client.SendBufferSize = 2 * 1024 * 1024;

                _isRunning = true;

                // Запускаем прием данных
                _receiveTask = Task.Run(() => ReceiveLoop(_cts.Token), _cts.Token);

                // Запускаем очистку просроченных рукопожатий
                _ = Task.Run(() => CleanupHandshakeLoop(_cts.Token), _cts.Token);

                // Запускаем мониторинг статистики
                _ = Task.Run(() => StatisticsLoop(_cts.Token), _cts.Token);

                _logger.Info($"Server started successfully on port {_config.ListenPort}");
                Console.WriteLine($"\n✅ Server is listening on {_config.ListenAddress}:{_config.ListenPort}");
                Console.WriteLine($"📊 Max clients: {_config.MaxClients}");
                Console.WriteLine($"🔐 Encryption: {_config.CipherSuite}\n");
            } catch (Exception ex) {
                _logger.Error($"Failed to start server: {ex.Message}");
                OnError?.Invoke($"Start failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Основной цикл приема данных
        /// </summary>
        private async Task ReceiveLoop(CancellationToken token) {
            while (!token.IsCancellationRequested && _isRunning) {
                try {
                    var result = await _udpServer.ReceiveAsync(token);
                    _ = Task.Run(() => ProcessPacket(result.Buffer, result.RemoteEndPoint), token);
                } catch (OperationCanceledException) {
                    break;
                } catch (Exception ex) {
                    _logger.Error($"Receive error: {ex.Message}");
                    await Task.Delay(1000, token);
                }
            }
        }

        /// <summary>
        /// Обработка полученного пакета
        /// </summary>
        private async Task ProcessPacket(byte[] data, IPEndPoint remoteEndpoint) {
            var clientKey = remoteEndpoint.ToString();

            try {
                // Проверяем, есть ли уже активная сессия
                if (_clients.TryGetValue(clientKey, out var session)) {
                    // Расшифровываем данные
                    var decrypted = session.Decrypt(data);
                    if (decrypted != null) {
                        await session.ProcessPacketAsync(decrypted);
                    }
                } else {
                    // Новый клиент - обрабатываем рукопожатие
                    await ProcessHandshake(data, remoteEndpoint, clientKey);
                }
            } catch (Exception ex) {
                _logger.Error($"Packet processing error for {clientKey}: {ex.Message}");
            }
        }

        /// <summary>
        /// Обработка рукопожатия нового клиента
        /// </summary>
        private async Task ProcessHandshake(byte[] data, IPEndPoint remoteEndpoint, string clientKey) {
            // Проверяем лимит клиентов
            if (_clients.Count >= _config.MaxClients) {
                _logger.Warning($"Max clients reached, rejecting connection from {clientKey}");
                return;
            }

            // Проверяем, не в процессе ли уже рукопожатие
            if (_pendingHandshakes.ContainsKey(clientKey)) {
                var startTime = _pendingHandshakes[clientKey];
                if ((DateTime.UtcNow - startTime).TotalSeconds > 10) {
                    _pendingHandshakes.TryRemove(clientKey, out _);
                } else {
                    return; // Рукопожатие уже в процессе
                }
            }

            // Создаем новую сессию
            var clientId = GenerateClientId();
            var session = new ClientSession(clientId, remoteEndpoint, _config, _udpServer);

            session.OnAuthenticated += () => OnClientAuthenticated(session, clientKey);
            session.OnDataReceived += (packet) => HandleClientData(packet, session);
            session.OnDisconnected += (reason) => OnClientDisconnected?.Invoke(clientId, reason);
            session.OnError += (error) => _logger.Error($"Session {clientId}: {error}");

            _pendingHandshakes.TryAdd(clientKey, DateTime.UtcNow);

            var success = await session.HandleHandshakeAsync(data);

            _pendingHandshakes.TryRemove(clientKey, out _);

            if (success) {
                _clients[clientKey] = session;
                _logger.Info($"Client {clientId} authenticated from {remoteEndpoint}");
            }
        }

        /// <summary>
        /// Обработка аутентификации клиента
        /// </summary>
        private void OnClientAuthenticated(ClientSession session, string clientKey) {
            // Назначаем виртуальный IP
            var virtualIp = AssignVirtualIp();
            session.SetVirtualIp(virtualIp);

            OnClientConnected?.Invoke(session.ClientId, session.RemoteEndpoint.ToString());

            _logger.Info($"Client {session.ClientId} assigned IP {virtualIp}");
        }

        /// <summary>
        /// Обработка данных от клиента
        /// </summary>
        private void HandleClientData(VpnPacket packet, ClientSession session) {
            // Маршрутизация данных
            if (packet.Type == PacketType.Data) {
                // Здесь можно реализовать маршрутизацию в интернет или другим клиентам
                _logger.Debug($"Data from {session.ClientId}: {packet.Data.Length} bytes");

                // Эхо для тестирования (отправляем обратно клиенту)
                var responsePacket = new VpnPacket(PacketType.Data, packet.Data);
                _ = session.SendPacketAsync(responsePacket);
            }
        }

        /// <summary>
        /// Назначение виртуального IP клиенту
        /// </summary>
        private string AssignVirtualIp() {
            var parts = _config.VirtualNetwork.Split('.');
            var baseIp = $"{parts[0]}.{parts[1]}.{parts[2]}";
            var clientId = _nextClientId++;

            return $"{baseIp}.{clientId + 2}"; // .1 для сервера, .2 и далее для клиентов
        }

        /// <summary>
        /// Генерация ID клиента
        /// </summary>
        private string GenerateClientId() {
            return $"CLIENT_{Interlocked.Increment(ref _nextClientId):D4}";
        }

        /// <summary>
        /// Очистка просроченных рукопожатий
        /// </summary>
        private async Task CleanupHandshakeLoop(CancellationToken token) {
            while (!token.IsCancellationRequested) {
                await Task.Delay(5000, token);

                var cutoff = DateTime.UtcNow.AddSeconds(-10);
                foreach (var kvp in _pendingHandshakes) {
                    if (kvp.Value < cutoff) {
                        _pendingHandshakes.TryRemove(kvp.Key, out _);
                        _logger.Debug($"Cleaned up stale handshake for {kvp.Key}");
                    }
                }
            }
        }

        /// <summary>
        /// Вывод статистики
        /// </summary>
        private async Task StatisticsLoop(CancellationToken token) {
            while (!token.IsCancellationRequested) {
                await Task.Delay(30000, token); // Каждые 30 секунд

                if (_isRunning) {
                    Console.WriteLine($"\n📊 Statistics: {_clients.Count} active clients");

                    foreach (var session in _clients.Values) {
                        var stats = session.GetStatistics();
                        Console.WriteLine($"   {session.ClientId}: {stats.BytesSent / 1024:F1}KB sent, {stats.BytesReceived / 1024:F1}KB received");
                    }
                }
            }
        }

        /// <summary>
        /// Остановка сервера
        /// </summary>
        public async Task StopAsync() {
            if (!_isRunning)
                return;

            _logger.Info("Stopping server...");
            _isRunning = false;

            _cts?.Cancel();

            // Отключаем всех клиентов
            foreach (var session in _clients.Values) {
                await session.DisconnectAsync("Server shutting down");
            }

            _clients.Clear();

            _udpServer?.Close();

            if (_receiveTask != null) {
                try {
                    await _receiveTask.WaitAsync(TimeSpan.FromSeconds(5));
                } catch { }
            }

            _logger.Info("Server stopped");
        }

        public void Dispose() {
            if (!_disposed) {
                _cts?.Dispose();
                _udpServer?.Dispose();
                foreach (var session in _clients.Values) {
                    session.Dispose();
                }
                _clients.Clear();
                _disposed = true;
            }
        }
    }
}