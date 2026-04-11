using System.Net;
using VpnClient.Models;
using VpnCore.Networking;
using VpnCore.Protocols;

namespace VpnClient.Services;

public class VpnService : IDisposable {
    private UdpTunnel? _tunnel;
    private HandshakeManager? _handshakeManager;
    private KeepAliveManager? _keepAliveManager;
    private bool _isConnected;
    private string _virtualIp = "0.0.0.0";
    private int _currentPing = 0;

    public event Action<ConnectionState, string>? OnStateChanged;
    public event Action<string>? OnLog;
    public event Action<byte[], bool>? OnDataReceived;

    public bool IsConnected => _isConnected;
    public string VirtualIp => _virtualIp;
    public int CurrentPing => _currentPing;


    public async Task<bool> ConnectAsync(string serverAddress, int port) {
        try {
            OnStateChanged?.Invoke(ConnectionState.Connecting, "");
            OnLog?.Invoke("🔌 Инициализация подключения...");
            OnLog?.Invoke($"🌐 Сервер: {serverAddress}:{port}");

            // 1. Создаём UDP туннель
            _tunnel = new UdpTunnel(0);
            _tunnel.OnPacketReceived += OnPacketReceived;
            _tunnel.OnError += (error) => OnLog?.Invoke($"❌ Tunnel error: {error}");

            // 2. Выполняем рукопожатие
            OnStateChanged?.Invoke(ConnectionState.Handshaking, "");
            OnLog?.Invoke("🔐 Выполнение рукопожатия...");

            _handshakeManager = new HandshakeManager(_tunnel);
            _handshakeManager.OnHandshakeProgress += (progress) => OnLog?.Invoke($"📡 Прогресс: {progress}%");
            _handshakeManager.OnHandshakeFailed += (error) => OnLog?.Invoke($"❌ Рукопожатие не удалось: {error}");

            var handshakeSuccess = await _handshakeManager.StartAsClientAsync();

            if (!handshakeSuccess) {
                OnLog?.Invoke("❌ Рукопожатие не удалось");
                OnStateChanged?.Invoke(ConnectionState.Error, "");
                return false;
            }

            var sessionKey = _handshakeManager.SessionKey;
            OnLog?.Invoke("✅ Рукопожатие завершено успешно");

            // 3. Подключаем туннель
            await _tunnel.ConnectAsync(serverAddress, port, sessionKey);

            // 4. Запускаем KeepAlive
            _keepAliveManager = new KeepAliveManager(_tunnel);
            _keepAliveManager.OnConnectionDead += () => OnLog?.Invoke("⚠️ Соединение потеряно");
            _keepAliveManager.Start();

            _isConnected = true;
            _virtualIp = "10.8.0.2";

            OnStateChanged?.Invoke(ConnectionState.Connected, _virtualIp);
            OnLog?.Invoke("✅ Подключение установлено");
            OnLog?.Invoke($"🔒 Виртуальный IP: {_virtualIp}");
            OnLog?.Invoke($"🔐 Шифрование: AES-256-GCM | Noise_IK");

            return true;
        } catch (Exception ex) {
            OnLog?.Invoke($"❌ Ошибка: {ex.Message}");
            OnStateChanged?.Invoke(ConnectionState.Error, "");
            return false;
        }
    }

    public async Task DisconnectAsync() {
        OnLog?.Invoke("⏹️ Отключение...");
        OnStateChanged?.Invoke(ConnectionState.Disconnecting, "");

        _keepAliveManager?.Dispose();

        if (_tunnel != null) {
            await _tunnel.DisconnectAsync();
            _tunnel.Dispose();
            _tunnel = null;
        }

        _isConnected = false;
        _virtualIp = "0.0.0.0";
        _currentPing = 0;

        OnStateChanged?.Invoke(ConnectionState.Disconnected, "");
        OnLog?.Invoke("🔌 Отключено");
    }

    private void OnPacketReceived(byte[] data, IPEndPoint endpoint) {
        try {
            var packet = VpnCore.Models.VpnPacket.Deserialize(data);

            if (packet.Type == VpnCore.Models.PacketType.Pong && packet.Data.Length >= 8) {
                var sentTicks = BitConverter.ToInt64(packet.Data, 0);
                var rtt = (DateTime.UtcNow.Ticks - sentTicks) / TimeSpan.TicksPerMillisecond;
                _currentPing = (int)rtt;
            }

            OnDataReceived?.Invoke(packet.Data, true);
        } catch (Exception ex) {
            OnLog?.Invoke($"⚠️ Ошибка обработки: {ex.Message}");
        }
    }

    public async Task SendDataAsync(byte[] data) {
        if (!_isConnected || _tunnel == null) return;

        try {
            var packet = new VpnCore.Models.VpnPacket(VpnCore.Models.PacketType.Data, data);
            await _tunnel.SendPacketAsync(packet);
            OnDataReceived?.Invoke(data, false);
        } catch (Exception ex) {
            OnLog?.Invoke($"❌ Ошибка отправки: {ex.Message}");
        }
    }

    public async Task SendPingAsync() {
        if (!_isConnected || _tunnel == null) return;

        var pingPacket = new VpnCore.Models.VpnPacket(VpnCore.Models.PacketType.Ping,
            BitConverter.GetBytes(DateTime.UtcNow.Ticks));
        await _tunnel.SendPacketAsync(pingPacket);
    }

    public void Dispose() {
        _keepAliveManager?.Dispose();
        _handshakeManager?.Dispose();
        _tunnel?.Dispose();
    }
}