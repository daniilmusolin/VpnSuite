using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Input;
using VpnClientWPF.Models;

namespace VpnClientWPF.ViewModels {
    public class MainViewModel : INotifyPropertyChanged {
        private readonly VpnService _vpnService;

        private ConnectionState _state = ConnectionState.Disconnected;
        private string _connectionTime = "--:--:--";
        private string _localIp = "0.0.0.0";
        private string _serverAddress = "127.0.0.1";
        private int _serverPort = 51820;
        private string _downloadSpeed = "0 KB/s";
        private string _uploadSpeed = "0 KB/s";
        private string _totalData = "↓ 0 MB  ↑ 0 MB";
        private string _ping = "-- ms";
        private bool _canConnect = true;
        private bool _canDisconnect = false;
        private DateTime? _connectStartTime;
        private System.Timers.Timer? _connectionTimer;
        private long _totalDown, _totalUp;

        private readonly ObservableCollection<string> _logs = new();

        public MainViewModel() {
            _vpnService = new VpnService();
            _vpnService.OnStateChanged += OnStateChanged;
            _vpnService.OnLog += AddLog;
            _vpnService.OnDataReceived += OnDataReceived;

            ConnectCommand = new RelayCommand(async () => await ConnectAsync(), () => CanConnect);
            DisconnectCommand = new RelayCommand(async () => await DisconnectAsync(), () => CanDisconnect);

            AddLog("VPN Клиент запущен");
            AddLog($"Сервер: {_serverAddress}:{_serverPort}");
            AddLog("Нажмите ПОДКЛЮЧИТЬ");
        }

        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ObservableCollection<string> Logs => _logs;

        public ConnectionState State {
            get => _state;
            set {
                _state = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StateColor));
                OnPropertyChanged(nameof(StateText));
                OnPropertyChanged(nameof(StateIcon));
                CanConnect = (value == ConnectionState.Disconnected);
                CanDisconnect = (value == ConnectionState.Connected);
            }
        }

        public Brush StateColor {
            get {
                return State switch {
                    ConnectionState.Connected => new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                    ConnectionState.Connecting => new SolidColorBrush(Color.FromRgb(255, 152, 0)),
                    ConnectionState.Handshaking => new SolidColorBrush(Color.FromRgb(33, 150, 243)),
                    ConnectionState.Disconnecting => new SolidColorBrush(Color.FromRgb(255, 152, 0)),
                    ConnectionState.Error => new SolidColorBrush(Color.FromRgb(244, 67, 54)),
                    _ => new SolidColorBrush(Color.FromRgb(158, 158, 158))
                };
            }
        }

        public string StateText {
            get {
                return State switch {
                    ConnectionState.Connected => "ПОДКЛЮЧЕНО",
                    ConnectionState.Connecting => "ПОДКЛЮЧЕНИЕ...",
                    ConnectionState.Handshaking => "РУКОПОЖАТИЕ...",
                    ConnectionState.Disconnecting => "ОТКЛЮЧЕНИЕ...",
                    ConnectionState.Error => "ОШИБКА",
                    _ => "ОТКЛЮЧЕНО"
                };
            }
        }

        public string StateIcon {
            get {
                return State switch {
                    ConnectionState.Connected => "Connected",
                    ConnectionState.Connecting => "Connecting",
                    ConnectionState.Handshaking => "Handshaking",
                    ConnectionState.Disconnecting => "Disconnecting",
                    ConnectionState.Error => "Error",
                    _ => "Unknown"
                };
            }
        }

        public string ConnectionTime { get => _connectionTime; set { _connectionTime = value; OnPropertyChanged(); } }
        public string LocalIp { get => _localIp; set { _localIp = value; OnPropertyChanged(); } }
        public string ServerAddress { get => _serverAddress; set { _serverAddress = value; OnPropertyChanged(); AddLog($"Сервер: {value}:{_serverPort}"); } }
        public int ServerPort { get => _serverPort; set { _serverPort = value; OnPropertyChanged(); AddLog($"Порт: {_serverAddress}:{value}"); } }
        public string DownloadSpeed { get => _downloadSpeed; set { _downloadSpeed = value; OnPropertyChanged(); } }
        public string UploadSpeed { get => _uploadSpeed; set { _uploadSpeed = value; OnPropertyChanged(); } }
        public string TotalData { get => _totalData; set { _totalData = value; OnPropertyChanged(); } }
        public string Ping { get => _ping; set { _ping = value; OnPropertyChanged(); } }
        public bool CanConnect { get => _canConnect; set { _canConnect = value; OnPropertyChanged(); (ConnectCommand as RelayCommand)?.RaiseCanExecuteChanged(); } }
        public bool CanDisconnect { get => _canDisconnect; set { _canDisconnect = value; OnPropertyChanged(); (DisconnectCommand as RelayCommand)?.RaiseCanExecuteChanged(); } }

        private async Task ConnectAsync() {
            if (string.IsNullOrWhiteSpace(ServerAddress)) {
                AddLog("Введите адрес сервера");
                return;
            }

            var success = await _vpnService.ConnectAsync(ServerAddress, ServerPort);

            if (success) {
                _connectStartTime = DateTime.Now;
                StartConnectionTimer();
                StartTrafficSimulation();
            }
        }

        private void StartTrafficSimulation() {
            var random = new Random();
            var timer = new System.Timers.Timer(100);
            timer.Elapsed += (s, e) => {
                if (State == ConnectionState.Connected) {
                    var down = random.Next(1000, 50000);
                    var up = random.Next(100, 10000);
                    _totalDown += down;
                    _totalUp += up;

                    App.Current.Dispatcher.Invoke(() => {
                        DownloadSpeed = FormatSpeed(down);
                        UploadSpeed = FormatSpeed(up);
                        TotalData = $"↓ {FormatBytes(_totalDown)}  ↑ {FormatBytes(_totalUp)}";
                        Ping = $"{random.Next(20, 100)} ms";
                    });
                }
            };
            timer.Start();
        }

        private void OnDataReceived(byte[] data, bool isDownload) { }

        private void OnStateChanged(ConnectionState state, string ip) {
            App.Current.Dispatcher.Invoke(() => {
                State = state;
                if (state == ConnectionState.Connected) {
                    LocalIp = ip;
                    AddLog($"Подключено! IP: {ip}");
                } else if (state == ConnectionState.Error) {
                    AddLog($"Ошибка подключения");
                }
            });
        }

        public async Task DisconnectAsyncMethod() {
            await DisconnectAsync();
        }

        private async Task DisconnectAsync() {
            AddLog("Отключение...");
            await _vpnService.DisconnectAsync();
            _connectStartTime = null;
            _connectionTimer?.Stop();
            AddLog("Клиент отключен");
        }

        private void StartConnectionTimer() {
            _connectionTimer = new System.Timers.Timer(1000);
            _connectionTimer.Elapsed += (s, e) => {
                if (_connectStartTime.HasValue && State == ConnectionState.Connected) {
                    var elapsed = DateTime.Now - _connectStartTime.Value;
                    App.Current.Dispatcher.Invoke(() => ConnectionTime = elapsed.ToString(@"hh\:mm\:ss"));
                }
            };
            _connectionTimer.Start();
        }

        private void AddLog(string message) {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            App.Current.Dispatcher.Invoke(() => {
                _logs.Insert(0, $"[{timestamp}] {message}");
                while (_logs.Count > 100) _logs.RemoveAt(_logs.Count - 1);
            });
        }

        private static string FormatSpeed(double bytesPerSec) {
            if (bytesPerSec >= 1024 * 1024) return $"{bytesPerSec / (1024 * 1024):F1} MB/s";
            if (bytesPerSec >= 1024) return $"{bytesPerSec / 1024:F1} KB/s";
            return $"{bytesPerSec:F0} B/s";
        }

        private static string FormatBytes(long bytes) {
            if (bytes >= 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
            if (bytes >= 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes} B";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
