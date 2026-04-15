using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using VpnClient.Models;
using VpnClient.Services;

namespace VpnClient.ViewModels {
    public class MainViewModel : INotifyPropertyChanged {
        private readonly VpnService _vpnService;
        private readonly TrafficService _trafficService;
        private readonly DispatcherTimer _uiTimer;
        private readonly DispatcherTimer _pingTimer;

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

        private readonly ObservableCollection<string> _logs = new();

        public MainViewModel() {
            _vpnService = new VpnService();
            _trafficService = new TrafficService();

            _vpnService.OnStateChanged += OnStateChanged;
            _vpnService.OnLog += AddLog;
            _vpnService.OnDataReceived += (data, isDownload) =>
                _trafficService.AddTraffic(data.Length, isDownload);

            _trafficService.OnUpdate += OnTrafficUpdate;

            ConnectCommand = new RelayCommand(async () => await Connect(), () => CanConnect);
            DisconnectCommand = new RelayCommand(async () => await Disconnect(), () => CanDisconnect);

            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _uiTimer.Tick += UpdateUi;
            _uiTimer.Start();

            _pingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _pingTimer.Tick += async (s, e) => await _vpnService.SendPingAsync();
            _pingTimer.Start();

            AddLog("VPN Клиент запущен");
            AddLog("Используется шифрование AES-256-GCM");
        }

        public RelayCommand ConnectCommand { get; }
        public RelayCommand DisconnectCommand { get; }
        public ObservableCollection<string> Logs => _logs;

        public ConnectionState State {
            get => _state;
            set {
                _state = value;
                OnPropertyChanged();
            }
        }

        public string ConnectionTime { get => _connectionTime; set { _connectionTime = value; OnPropertyChanged(); } }
        public string LocalIp { get => _localIp; set { _localIp = value; OnPropertyChanged(); } }
        public string ServerAddress { get => _serverAddress; set { _serverAddress = value; OnPropertyChanged(); } }
        public int ServerPort { get => _serverPort; set { _serverPort = value; OnPropertyChanged(); } }
        public string DownloadSpeed { get => _downloadSpeed; set { _downloadSpeed = value; OnPropertyChanged(); } }
        public string UploadSpeed { get => _uploadSpeed; set { _uploadSpeed = value; OnPropertyChanged(); } }
        public string TotalData { get => _totalData; set { _totalData = value; OnPropertyChanged(); } }
        public string Ping { get => _ping; set { _ping = value; OnPropertyChanged(); } }
        public bool CanConnect { get => _canConnect; set { _canConnect = value; OnPropertyChanged(); ConnectCommand?.RaiseCanExecuteChanged(); } }
        public bool CanDisconnect { get => _canDisconnect; set { _canDisconnect = value; OnPropertyChanged(); DisconnectCommand?.RaiseCanExecuteChanged(); } }

        private async System.Threading.Tasks.Task Connect() {
            AddLog($"Подключение к {_serverAddress}:{_serverPort}...");
            var success = await _vpnService.ConnectAsync(_serverAddress, _serverPort);

            if (success) {
                _connectStartTime = DateTime.Now;
                AddLog("Подключение установлено!");
            }
        }

        private async System.Threading.Tasks.Task Disconnect() {
            AddLog("Отключение...");
            await _vpnService.DisconnectAsync();
            _connectStartTime = null;
            _trafficService.Reset();
            AddLog("Отключено");
        }

        private void OnStateChanged(ConnectionState state, string ip) {
            State = state;
            CanConnect = state == ConnectionState.Disconnected;
            CanDisconnect = state == ConnectionState.Connected;
            LocalIp = state == ConnectionState.Connected ? ip : "0.0.0.0";
        }

        private void AddLog(string message) {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            App.Current.Dispatcher.Invoke(() => {
                _logs.Insert(0, $"[{timestamp}] {message}");
                while (_logs.Count > 100) _logs.RemoveAt(_logs.Count - 1);
            });
        }

        private void OnTrafficUpdate(double downSpeed, double upSpeed, long totalDown, long totalUp) {
            App.Current.Dispatcher.Invoke(() => {
                DownloadSpeed = FormatSpeed(downSpeed);
                UploadSpeed = FormatSpeed(upSpeed);
                TotalData = $"↓ {FormatBytes(totalDown)}  ↑ {FormatBytes(totalUp)}";
            });
        }

        private void UpdateUi(object sender, EventArgs e) {
            if (_connectStartTime.HasValue && State == ConnectionState.Connected) {
                var elapsed = DateTime.Now - _connectStartTime.Value;
                ConnectionTime = elapsed.ToString(@"hh\:mm\:ss");
            } else if (State != ConnectionState.Connected) {
                ConnectionTime = "--:--:--";
            }

            Ping = $"{_vpnService.CurrentPing} ms";
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
        protected void OnPropertyChanged([CallerMemberName] string? name = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}