using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using VpnCore.Models;
using VpnCore.Utils;

namespace VpnServer;

public sealed class ServerCore : IDisposable {
    private UdpClient? _udpServer;
    private readonly ServerConfig _config;
    private readonly ConcurrentDictionary<string, ClientSession> _clients;
    private readonly Logger _logger;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private int _nextClientId;
    private bool _isRunning;
    private bool _disposed;
    private DateTime _startTime;

    public event Action<string, string>? OnClientConnected;
    public event Action<string, string>? OnClientDisconnected;
    public event Action<string>? OnError;

    public int ActiveClients => _clients.Count;
    public bool IsRunning => _isRunning;
    public ServerConfig Config => _config;

    public ServerCore(ServerConfig config) {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _clients = new ConcurrentDictionary<string, ClientSession>();
        _logger = Logger.Instance;
        _config.Validate();
        _startTime = DateTime.UtcNow;
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
            _startTime = DateTime.UtcNow;

            _receiveTask = Task.Run(() => ReceiveLoop(_cts.Token), _cts.Token);
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
                var result = await _udpServer!.ReceiveAsync(token);
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
            // Обработка текстовых сообщений (рукопожатие)
            string textMessage = null;
            try {
                textMessage = Encoding.UTF8.GetString(data);
                if (textMessage == "VPN_CLIENT_HELLO") {
                    byte[] response = Encoding.UTF8.GetBytes("VPN_SERVER_HELLO");
                    await _udpServer!.SendAsync(response, response.Length, remoteEndpoint);
                    return;
                }
            } catch { }

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

        var clientId = GenerateClientId();
        var session = new ClientSession(clientId, remoteEndpoint, _config, _udpServer!);

        session.SetVirtualIp(AssignVirtualIp());
        _clients[clientKey] = session;

        _logger.Info($"Клиент {clientId} подключен с {remoteEndpoint}");
        Console.WriteLine($"[+] Клиент подключился: {clientId} с {remoteEndpoint}");
        Console.WriteLine($"   Назначен IP: {session.VirtualIp}");

        OnClientConnected?.Invoke(clientId, remoteEndpoint.ToString());
    }

    private string AssignVirtualIp() {
        var parts = _config.VirtualNetwork.Split('.');
        var baseIp = $"{parts[0]}.{parts[1]}.{parts[2]}";
        return $"{baseIp}.{Interlocked.Increment(ref _nextClientId) + 2}";
    }

    private string GenerateClientId() => $"КЛИЕНТ_{Interlocked.Increment(ref _nextClientId):D4}";

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

    // ============= API Methods =============

    public List<ClientInfo> GetAllClients() {
        return _clients.Values.Select(session => new ClientInfo {
            ClientId = session.ClientId,
            RemoteEndpoint = session.RemoteEndpoint?.ToString() ?? "Unknown",
            VirtualIp = session.VirtualIp ?? "N/A",
            BytesSent = session.BytesSent,
            BytesReceived = session.BytesReceived,
            PacketsSent = session.PacketsSent,
            PacketsReceived = session.PacketsReceived,
            LastActivity = session.LastActivity,
            IsAuthenticated = session.IsAuthenticated
        }).ToList();
    }

    public ClientInfo? GetClient(string clientId) {
        var session = _clients.Values.FirstOrDefault(s => s.ClientId == clientId);
        if (session == null) return null;

        return new ClientInfo {
            ClientId = session.ClientId,
            RemoteEndpoint = session.RemoteEndpoint?.ToString() ?? "Unknown",
            VirtualIp = session.VirtualIp ?? "N/A",
            BytesSent = session.BytesSent,
            BytesReceived = session.BytesReceived,
            PacketsSent = session.PacketsSent,
            PacketsReceived = session.PacketsReceived,
            LastActivity = session.LastActivity,
            IsAuthenticated = session.IsAuthenticated
        };
    }

    public bool KickClient(string clientId) {
        var session = _clients.Values.FirstOrDefault(s => s.ClientId == clientId);
        if (session == null) return false;

        var clientKey = _clients.FirstOrDefault(x => x.Value.ClientId == clientId).Key;
        if (clientKey != null && _clients.TryRemove(clientKey, out var removedSession)) {
            _ = removedSession.DisconnectAsync("Kicked by administrator");
            OnClientDisconnected?.Invoke(removedSession.ClientId, "Kicked by admin");
            _logger.Info($"Client {clientId} kicked by API");
            return true;
        }
        return false;
    }

    public bool BanClient(string clientId) {
        var session = _clients.Values.FirstOrDefault(s => s.ClientId == clientId);
        if (session == null) return false;

        var clientKey = _clients.FirstOrDefault(x => x.Value.ClientId == clientId).Key;
        if (clientKey != null && _clients.TryRemove(clientKey, out var removedSession)) {
            _ = removedSession.DisconnectAsync("Banned by administrator");
            OnClientDisconnected?.Invoke(removedSession.ClientId, "Banned by admin");
            AddToBlacklist(clientId);
            _logger.Info($"Client {clientId} banned by API");
            return true;
        }
        return false;
    }

    private void AddToBlacklist(string clientId) {
        var blacklistFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "blacklist.txt");
        var entry = $"{clientId}|{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
        File.AppendAllText(blacklistFile, entry + Environment.NewLine);
    }

    public object GetServerStats() {
        var totalSent = _clients.Values.Sum(s => s.BytesSent);
        var totalReceived = _clients.Values.Sum(s => s.BytesReceived);

        return new {
            IsRunning = _isRunning,
            ActiveClients = _clients.Count,
            TotalBytesSent = totalSent,
            TotalBytesReceived = totalReceived,
            CurrentSendSpeed = 0L,
            CurrentReceiveSpeed = 0L,
            Uptime = DateTime.UtcNow - _startTime,
            CipherSuite = _config.CipherSuite,
            Timestamp = DateTime.UtcNow
        };
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

public class ClientInfo {
    public string ClientId { get; set; } = "";
    public string RemoteEndpoint { get; set; } = "";
    public string VirtualIp { get; set; } = "";
    public long BytesSent { get; set; }
    public long BytesReceived { get; set; }
    public long PacketsSent { get; set; }
    public long PacketsReceived { get; set; }
    public DateTime LastActivity { get; set; }
    public bool IsAuthenticated { get; set; }
}