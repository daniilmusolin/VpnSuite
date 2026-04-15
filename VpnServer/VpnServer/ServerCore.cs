using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using VpnCore.Models;
using VpnCore.Utils;

namespace VpnServer;

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

    public async Task StartAsync(CancellationToken cancellationToken = default) {
        if (_isRunning)
            throw new InvalidOperationException("Сервер уже запущен");

        _logger.Info($"Запуск сервера на {_config.ListenAddress}:{_config.ListenPort}");
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try {
            var endPoint = new IPEndPoint(IPAddress.Parse(_config.ListenAddress), _config.ListenPort);
            _udpServer = new UdpClient(endPoint);
            _udpServer.Client.ReceiveBufferSize = 2 * 1024 * 1024;
            _udpServer.Client.SendBufferSize = 2 * 1024 * 1024;

            _isRunning = true;

            _receiveTask = Task.Run(() => ReceiveLoop(_cts.Token), _cts.Token);
            _ = Task.Run(() => CleanupHandshakeLoop(_cts.Token), _cts.Token);
            _ = Task.Run(() => StatisticsLoop(_cts.Token), _cts.Token);
            _ = Task.Run(() => CleanupInactiveClientsLoop(_cts.Token), _cts.Token);

            _logger.Info($"Сервер успешно запущен на порту {_config.ListenPort}");
            Console.WriteLine($"\n✅ Сервер слушает {_config.ListenAddress}:{_config.ListenPort}");
            Console.WriteLine($"📊 Максимум клиентов: {_config.MaxClients}");
            Console.WriteLine($"🔐 Шифрование: {_config.CipherSuite}\n");
        } catch (Exception ex) {
            _logger.Error($"Не удалось запустить сервер: {ex.Message}");
            OnError?.Invoke($"Ошибка запуска: {ex.Message}");
            throw;
        }
    }

    private async Task ReceiveLoop(CancellationToken token) {
        while (!token.IsCancellationRequested && _isRunning) {
            try {
                var result = await _udpServer.ReceiveAsync(token);
                _ = Task.Run(() => ProcessPacket(result.Buffer, result.RemoteEndPoint), token);
            } catch (OperationCanceledException) { break; } catch (Exception ex) {
                _logger.Error($"Ошибка приема: {ex.Message}");
                await Task.Delay(1000, token);
            }
        }
    }

    private async Task ProcessPacket(byte[] data, IPEndPoint remoteEndpoint) {
        var clientKey = remoteEndpoint.ToString();

        try {
            string textMessage = null;
            try {
                textMessage = Encoding.UTF8.GetString(data);
                _logger.Info($"Получено: '{textMessage}' от {clientKey}");
            } catch { }

            // Обработка текстовых сообщений
            if (textMessage != null && textMessage.StartsWith("VPN_")) {
                if (textMessage == "VPN_CLIENT_HELLO") {
                    byte[] response = Encoding.UTF8.GetBytes("VPN_SERVER_HELLO");
                    await _udpServer.SendAsync(response, response.Length, remoteEndpoint);
                    _logger.Info($"✅ Отправлен VPN_SERVER_HELLO на {clientKey}");
                    return;
                } else if (textMessage == "KEEP_ALIVE") {
                    byte[] response = Encoding.UTF8.GetBytes("KEEP_ALIVE_OK");
                    await _udpServer.SendAsync(response, response.Length, remoteEndpoint);
                    return;
                } else if (textMessage == "DISCONNECT") {
                    _logger.Info($"🔌 Клиент {clientKey} отключился");
                    if (_clients.TryRemove(clientKey, out var removedSession)) {
                        OnClientDisconnected?.Invoke(removedSession.ClientId, "Клиент отключился");
                        Console.WriteLine($"[-] Клиент отключился: {removedSession.ClientId}");
                    }
                    return;
                }
            }

            // Обычная обработка VPN клиентов
            if (_clients.TryGetValue(clientKey, out var existingSession)) {
                var decrypted = existingSession.Decrypt(data);
                if (decrypted != null) {
                    await existingSession.ProcessPacketAsync(decrypted);
                }
            } else {
                await ProcessHandshake(data, remoteEndpoint, clientKey);
            }
        } catch (Exception ex) {
            _logger.Error($"Ошибка обработки пакета для {clientKey}: {ex.Message}");
        }
    }

    private async Task ProcessHandshake(byte[] data, IPEndPoint remoteEndpoint, string clientKey) {
        if (_clients.Count >= _config.MaxClients) {
            _logger.Warning($"Достигнут лимит клиентов, отклоняем {clientKey}");
            return;
        }

        // Просто создаем сессию без сложного рукопожатия
        var clientId = GenerateClientId();
        var session = new ClientSession(clientId, remoteEndpoint, _config, _udpServer);

        // Сразу считаем аутентифицированным
        session.SetVirtualIp(AssignVirtualIp());
        _clients[clientKey] = session;

        _logger.Info($"Клиент {clientId} подключен с {remoteEndpoint}");
        Console.WriteLine($"[+] Клиент подключился: {clientId} с {remoteEndpoint}");
        Console.WriteLine($"   Назначен IP: {session.VirtualIp}");

        OnClientConnected?.Invoke(clientId, remoteEndpoint.ToString());
    }

    private void OnClientAuthenticated(ClientSession session, string clientKey) {
        var virtualIp = AssignVirtualIp();
        session.SetVirtualIp(virtualIp);
        OnClientConnected?.Invoke(session.ClientId, session.RemoteEndpoint.ToString());
        _logger.Info($"Клиенту {session.ClientId} назначен IP {virtualIp}");
        Console.WriteLine($"   Назначен IP: {virtualIp}");
    }

    private void HandleClientData(VpnPacket packet, ClientSession session) {
        if (packet.Type == PacketType.Data) {
            _logger.Debug($"Данные от {session.ClientId}: {packet.Data.Length} байт");
            var responsePacket = new VpnPacket(PacketType.Data, packet.Data);
            _ = session.SendPacketAsync(responsePacket);
        }
    }

    private string AssignVirtualIp() {
        var parts = _config.VirtualNetwork.Split('.');
        var baseIp = $"{parts[0]}.{parts[1]}.{parts[2]}";
        return $"{baseIp}.{_nextClientId++ + 2}";
    }

    private string GenerateClientId() => $"КЛИЕНТ_{Interlocked.Increment(ref _nextClientId):D4}";

    private async Task CleanupHandshakeLoop(CancellationToken token) {
        while (!token.IsCancellationRequested) {
            await Task.Delay(5000, token);
            var cutoff = DateTime.UtcNow.AddSeconds(-10);
            foreach (var kvp in _pendingHandshakes) {
                if (kvp.Value < cutoff) {
                    _pendingHandshakes.TryRemove(kvp.Key, out _);
                }
            }
        }
    }

    private async Task CleanupInactiveClientsLoop(CancellationToken token) {
        while (!token.IsCancellationRequested) {
            await Task.Delay(30000, token);
            var timeout = DateTime.UtcNow.AddSeconds(-60);

            foreach (var kvp in _clients) {
                if (kvp.Value.LastActivity < timeout) {
                    _logger.Info($"Клиент {kvp.Value.ClientId} неактивен, отключаем");
                    await kvp.Value.DisconnectAsync("Таймаут неактивности");
                    _clients.TryRemove(kvp.Key, out _);
                    OnClientDisconnected?.Invoke(kvp.Value.ClientId, "Таймаут неактивности");
                    Console.WriteLine($"[-] Клиент отключен (неактивен): {kvp.Value.ClientId}");
                }
            }
        }
    }

    private async Task StatisticsLoop(CancellationToken token) {
        while (!token.IsCancellationRequested) {
            await Task.Delay(30000, token);
            if (_isRunning) {
                Console.WriteLine($"\n📊 Активных клиентов: {_clients.Count}");
                foreach (var session in _clients.Values) {
                    var stats = session.GetStatistics();
                    Console.WriteLine($"   {session.ClientId}: {stats.BytesSent / 1024:F1}KB отправлено, {stats.BytesReceived / 1024:F1}KB получено");
                }
            }
        }
    }

    public async Task StopAsync() {
        if (!_isRunning) return;
        _logger.Info("Остановка сервера...");
        _isRunning = false;
        _cts?.Cancel();
        foreach (var session in _clients.Values) {
            await session.DisconnectAsync("Сервер останавливается");
        }
        _clients.Clear();
        _udpServer?.Close();
        _logger.Info("Сервер остановлен");
    }

    public void Dispose() {
        if (!_disposed) {
            _cts?.Dispose();
            _udpServer?.Dispose();
            foreach (var session in _clients.Values) session.Dispose();
            _clients.Clear();
            _disposed = true;
        }
    }
}