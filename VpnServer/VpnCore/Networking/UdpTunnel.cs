using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using VpnCore.Cryptography;
using VpnCore.Models;
using VpnCore.Utils;

namespace VpnCore.Networking {
    /// <summary>
    /// Состояние UDP туннеля
    /// </summary>
    public enum TunnelState {
        Disconnected,   // Нет соединения
        Connecting,     // Установка соединения
        Connected,      // Соединено
        Reconnecting,   // Переподключение
        Disconnecting,  // Отключение
        Failed          // Ошибка
    }

    /// <summary>
    /// UDP туннель с шифрованием и многопоточностью
    /// Обеспечивает надежную передачу данных через UDP с аутентификацией
    /// </summary>
    public sealed class UdpTunnel : IDisposable {
        private UdpClient _udpClient;
        private IPEndPoint _remoteEndpoint;
        private AesGcmEncryption _encryption;
        private CancellationTokenSource _cts;

        // Очереди для асинхронной обработки
        private readonly ConcurrentQueue<byte[]> _sendQueue;
        private readonly ConcurrentQueue<byte[]> _receiveQueue;

        // Семафоры для ограничения параллелизма
        private readonly SemaphoreSlim _sendSemaphore;
        private readonly SemaphoreSlim _receiveSemaphore;

        private TunnelState _state;
        private int _reconnectAttempts;
        private readonly object _stateLock = new object();
        private readonly Logger _logger;

        // События для уведомления о состоянии
        public event Action<byte[], IPEndPoint> OnPacketReceived;
        public event Action<TunnelState> OnStateChanged;
        public event Action<string> OnError;

        /// <summary>
        /// Текущее состояние туннеля
        /// </summary>
        public TunnelState State {
            get => _state;
            private set {
                lock (_stateLock) {
                    if (_state != value) {
                        _state = value;
                        OnStateChanged?.Invoke(value);
                        _logger.Info($"Tunnel state changed to {value}");
                    }
                }
            }
        }

        /// <summary>
        /// Конструктор UDP туннеля
        /// </summary>
        /// <param name="localPort">Локальный порт (0 = автоматический)</param>
        public UdpTunnel(int localPort = 0) {
            _logger = Logger.Instance;
            _udpClient = new UdpClient(localPort);

            // Настройка буферов для производительности
            _udpClient.Client.ReceiveBufferSize = 2 * 1024 * 1024; // 2 MB
            _udpClient.Client.SendBufferSize = 2 * 1024 * 1024;

            _sendQueue = new ConcurrentQueue<byte[]>();
            _receiveQueue = new ConcurrentQueue<byte[]>();
            _sendSemaphore = new SemaphoreSlim(64);
            _receiveSemaphore = new SemaphoreSlim(64);
            _cts = new CancellationTokenSource();

            State = TunnelState.Disconnected;
        }

        /// <summary>
        /// Подключение к удаленному серверу
        /// </summary>
        public async Task ConnectAsync(string remoteAddress, int remotePort, byte[] sessionKey) {
            if (State != TunnelState.Disconnected)
                throw new InvalidOperationException($"Cannot connect in state {State}");

            State = TunnelState.Connecting;

            try {
                _remoteEndpoint = new IPEndPoint(IPAddress.Parse(remoteAddress), remotePort);
                _encryption = new AesGcmEncryption(sessionKey);
                _udpClient.Connect(_remoteEndpoint);

                // Запускаем рабочие потоки
                _ = Task.Run(() => SendWorker(_cts.Token));
                _ = Task.Run(() => ReceiveWorker(_cts.Token));
                _ = Task.Run(() => ProcessWorker(_cts.Token));

                State = TunnelState.Connected;
                _reconnectAttempts = 0;
                _logger.Info($"Connected to {remoteAddress}:{remotePort}");
            } catch (Exception ex) {
                _logger.Error($"Connection failed: {ex.Message}");
                OnError?.Invoke(ex.Message);
                State = TunnelState.Failed;
                throw;
            }
        }

        /// <summary>
        /// Отправка данных через туннель
        /// </summary>
        public async Task SendAsync(byte[] data) {
            if (State != TunnelState.Connected)
                throw new InvalidOperationException($"Cannot send in state {State}");

            await _sendSemaphore.WaitAsync();
            try {
                var encrypted = _encryption.Encrypt(data);
                _sendQueue.Enqueue(encrypted);
            } finally {
                _sendSemaphore.Release();
            }
        }

        /// <summary>
        /// Отправка VpnPacket через туннель
        /// </summary>
        public async Task SendPacketAsync(VpnPacket packet) {
            var data = packet.Serialize();
            await SendAsync(data);
        }

        /// <summary>
        /// Воркер отправки данных
        /// </summary>
        private async Task SendWorker(CancellationToken token) {
            while (!token.IsCancellationRequested && State == TunnelState.Connected) {
                try {
                    if (_sendQueue.TryDequeue(out var data)) {
                        await _udpClient.SendAsync(data, data.Length);
                        await Task.Delay(1, token); // Даем время другим задачам
                    } else {
                        await Task.Delay(10, token);
                    }
                } catch (OperationCanceledException) {
                    break;
                } catch (Exception ex) {
                    _logger.Error($"Send worker error: {ex.Message}");
                    await Task.Delay(1000, token);
                }
            }
        }

        /// <summary>
        /// Воркер получения данных
        /// </summary>
        private async Task ReceiveWorker(CancellationToken token) {
            while (!token.IsCancellationRequested && State == TunnelState.Connected) {
                try {
                    var result = await _udpClient.ReceiveAsync(token);
                    _receiveQueue.Enqueue(result.Buffer);
                } catch (OperationCanceledException) {
                    break;
                } catch (Exception ex) {
                    _logger.Error($"Receive worker error: {ex.Message}");
                    await Task.Delay(1000, token);
                }
            }
        }

        /// <summary>
        /// Воркер обработки полученных данных
        /// </summary>
        private async Task ProcessWorker(CancellationToken token) {
            while (!token.IsCancellationRequested && State == TunnelState.Connected) {
                try {
                    if (_receiveQueue.TryDequeue(out var encrypted)) {
                        await _receiveSemaphore.WaitAsync(token);
                        try {
                            var decrypted = _encryption.Decrypt(encrypted);
                            OnPacketReceived?.Invoke(decrypted, _remoteEndpoint);
                        } finally {
                            _receiveSemaphore.Release();
                        }
                    } else {
                        await Task.Delay(10, token);
                    }
                } catch (OperationCanceledException) {
                    break;
                } catch (Exception ex) {
                    _logger.Error($"Process worker error: {ex.Message}");
                    await Task.Delay(1000, token);
                }
            }
        }

        /// <summary>
        /// Отключение от сервера
        /// </summary>
        public async Task DisconnectAsync() {
            State = TunnelState.Disconnecting;
            _cts.Cancel();
            _udpClient?.Close();
            State = TunnelState.Disconnected;
            await Task.CompletedTask;
        }

        public void Dispose() {
            _cts?.Cancel();
            _cts?.Dispose();
            _udpClient?.Dispose();
            _sendSemaphore?.Dispose();
            _receiveSemaphore?.Dispose();
            _encryption?.Dispose();
        }
    }
}