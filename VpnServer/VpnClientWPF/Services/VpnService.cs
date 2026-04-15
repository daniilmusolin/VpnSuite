using System.Net.Sockets;
using System.Text;
using VpnClientWPF.Models;

public class VpnService : IDisposable {
    private UdpClient? _udpClient;
    private bool _isConnected;
    private string _virtualIp = "0.0.0.0";
    private int _currentPing = 0;
    private CancellationTokenSource? _cts;

    public event Action<ConnectionState, string>? OnStateChanged;
    public event Action<string>? OnLog;
    public event Action<byte[], bool>? OnDataReceived;

    public bool IsConnected => _isConnected;
    public string VirtualIp => _virtualIp;
    public int CurrentPing => _currentPing;

    public async Task<bool> ConnectAsync(string serverAddress, int port) {
        try {
            OnStateChanged?.Invoke(ConnectionState.Connecting, "");
            OnLog?.Invoke($"🔌 Подключение к {serverAddress}:{port}...");

            _udpClient = new UdpClient();

            OnLog?.Invoke($"📤 Отправка HELLO серверу...");
            byte[] helloMsg = Encoding.UTF8.GetBytes("VPN_CLIENT_HELLO");
            await _udpClient.SendAsync(helloMsg, helloMsg.Length, serverAddress, port);

            OnLog?.Invoke($"📥 Ожидание ответа от сервера...");
            using var cts = new CancellationTokenSource(5000);
            var receiveTask = _udpClient.ReceiveAsync(cts.Token);
            var completedTask = await Task.WhenAny(receiveTask.AsTask(), Task.Delay(5000));

            if (completedTask != receiveTask.AsTask()) {
                OnLog?.Invoke($"❌ Таймаут! Сервер не ответил");
                OnStateChanged?.Invoke(ConnectionState.Error, "");
                return false;
            }

            var result = await receiveTask;
            string response = Encoding.UTF8.GetString(result.Buffer);
            OnLog?.Invoke($"📥 Получен ответ: {response}");

            if (response != "VPN_SERVER_HELLO") {
                OnLog?.Invoke($"❌ Неверный ответ от сервера");
                OnStateChanged?.Invoke(ConnectionState.Error, "");
                return false;
            }

            _isConnected = true;
            _virtualIp = "10.8.0.2";

            OnStateChanged?.Invoke(ConnectionState.Connected, _virtualIp);
            OnLog?.Invoke($"✅ ПОДКЛЮЧЕНО к {serverAddress}:{port}!");
            OnLog?.Invoke($"🔒 Виртуальный IP: {_virtualIp}");

            StartKeepAlive(serverAddress, port);
            StartTrafficSimulation();

            return true;
        } catch (SocketException ex) {
            OnLog?.Invoke($"❌ Ошибка сокета: {ex.Message}");
            OnStateChanged?.Invoke(ConnectionState.Error, "");
            return false;
        } catch (Exception ex) {
            OnLog?.Invoke($"❌ Ошибка: {ex.Message}");
            OnStateChanged?.Invoke(ConnectionState.Error, "");
            return false;
        }
    }

    private void StartKeepAlive(string serverAddress, int port) {
        _cts = new CancellationTokenSource();
        _ = Task.Run(async () => {
            while (!_cts.Token.IsCancellationRequested && _isConnected) {
                await Task.Delay(25000, _cts.Token);
                if (_udpClient != null && _isConnected) {
                    try {
                        byte[] keepAlive = Encoding.UTF8.GetBytes("KEEP_ALIVE");
                        await _udpClient.SendAsync(keepAlive, keepAlive.Length, serverAddress, port);
                    } catch { }
                }
            }
        });
    }

    private void StartTrafficSimulation() {
        var random = new Random();
        _ = Task.Run(async () => {
            long down = 0, up = 0;
            while (_isConnected) {
                await Task.Delay(100);
                if (_isConnected) {
                    var d = random.Next(1000, 50000);
                    var u = random.Next(100, 10000);
                    down += d;
                    up += u;

                    OnDataReceived?.Invoke(new byte[d], true);
                    OnDataReceived?.Invoke(new byte[u], false);
                }
            }
        });
    }

    public async Task DisconnectAsync() {
        OnLog?.Invoke("⏹️ Отключение...");
        OnStateChanged?.Invoke(ConnectionState.Disconnecting, "");

        _cts?.Cancel();

        if (_udpClient != null && _isConnected) {
            try {
                byte[] disconnectMsg = Encoding.UTF8.GetBytes("DISCONNECT");
                await _udpClient.SendAsync(disconnectMsg, disconnectMsg.Length);
                OnLog?.Invoke($"📤 Отправлен DISCONNECT серверу");
            } catch { }

            try {
                _udpClient.Close();
                _udpClient.Dispose();
            } catch { }
            _udpClient = null;
        }

        await Task.Delay(500);

        _isConnected = false;
        _virtualIp = "0.0.0.0";

        OnStateChanged?.Invoke(ConnectionState.Disconnected, "");
        OnLog?.Invoke("🔌 Отключено");
    }

    public void Dispose() {
        _cts?.Cancel();
        _cts?.Dispose();
        _udpClient?.Dispose();
    }
}