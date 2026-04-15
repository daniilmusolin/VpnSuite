using System;
using System.Threading;
using System.Timers;

namespace VpnClient.Services {
    public class TrafficService : IDisposable {
        private long _totalDownload;
        private long _totalUpload;
        private long _lastDownload;
        private long _lastUpload;
        private double _currentDownloadSpeed;
        private double _currentUploadSpeed;
        private readonly System.Timers.Timer _timer;

        public event Action<double, double, long, long>? OnUpdate;

        public TrafficService() {
            _timer = new System.Timers.Timer(1000);
            _timer.Elapsed += UpdateSpeed;
            _timer.Start();
        }

        public void AddTraffic(int bytes, bool isDownload) {
            if (isDownload)
                Interlocked.Add(ref _totalDownload, bytes);
            else
                Interlocked.Add(ref _totalUpload, bytes);
        }

        private void UpdateSpeed(object? sender, ElapsedEventArgs e) {
            var currentDownload = _totalDownload;
            var currentUpload = _totalUpload;

            _currentDownloadSpeed = currentDownload - _lastDownload;
            _currentUploadSpeed = currentUpload - _lastUpload;

            _lastDownload = currentDownload;
            _lastUpload = currentUpload;

            OnUpdate?.Invoke(_currentDownloadSpeed, _currentUploadSpeed, _totalDownload, _totalUpload);
        }

        public void Reset() {
            _totalDownload = 0;
            _totalUpload = 0;
            _lastDownload = 0;
            _lastUpload = 0;
            _currentDownloadSpeed = 0;
            _currentUploadSpeed = 0;
        }

        public void Dispose() {
            _timer?.Stop();
            _timer?.Dispose();
        }
    }
}