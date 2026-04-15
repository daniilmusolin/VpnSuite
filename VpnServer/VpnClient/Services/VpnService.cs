using VpnClient.Models;

namespace VpnClient.Services {
    public class VpnService : IDisposable {
        private bool _isConnected;
        private string _virtualIp = "0.0.0.0";
        private int _currentPing = 0;
        private System.Timers.Timer? _pingSimulator;

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

                // Симуляция подключения
                await Task.Delay(2000);

                OnStateChanged?.Invoke(ConnectionState.Handshaking, "");
                OnLog?.Invoke("🔐 Выполнение рукопожатия...");
                await Task.Delay(1000);

                _isConnected = true;
                _virtualIp = "10.8.0.2";
                _currentPing = 25;

                // Симуляция пинга
                _pingSimulator = new System.Timers.Timer(2000);
                _pingSimulator.Elapsed += (s, e) => _currentPing = new Random().Next(20, 100);
                _pingSimulator.Start();

                OnStateChanged?.Invoke(ConnectionState.Connected, _virtualIp);
                OnLog?.Invoke("✅ Подключение установлено!");
                OnLog?.Invoke($"🔒 Виртуальный IP: {_virtualIp}");
                OnLog?.Invoke($"🔐 Шифрование: AES-256-GCM");

                // Симуляция трафика
                SimulateTraffic();

                return true;
            } catch (Exception ex) {
                OnLog?.Invoke($"❌ Ошибка: {ex.Message}");
                OnStateChanged?.Invoke(ConnectionState.Error, "");
                return false;
            }
        }

        private void SimulateTraffic() {
            var random = new Random();
            var timer = new System.Timers.Timer(100);
            timer.Elapsed += (s, e) => {
                if (_isConnected) {
                    var data = new byte[random.Next(100, 5000)];
                    random.NextBytes(data);
                    OnDataReceived?.Invoke(data, true);

                    if (random.Next(2) == 0) {
                        OnDataReceived?.Invoke(data, false);
                    }
                } else {
                    timer.Stop();
                    timer.Dispose();
                }
            };
            timer.Start();
        }

        public async Task DisconnectAsync() {
            OnLog?.Invoke("⏹️ Отключение...");
            OnStateChanged?.Invoke(ConnectionState.Disconnecting, "");

            _pingSimulator?.Stop();
            _pingSimulator?.Dispose();

            await Task.Delay(500);

            _isConnected = false;
            _virtualIp = "0.0.0.0";
            _currentPing = 0;

            OnStateChanged?.Invoke(ConnectionState.Disconnected, "");
            OnLog?.Invoke("🔌 Отключено");
        }

        public Task SendPingAsync() {
            return Task.CompletedTask;
        }

        public void Dispose() {
            _pingSimulator?.Dispose();
        }
    }
}