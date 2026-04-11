using VpnCore.Networking;
using VpnCore.Utils;

namespace VpnCore.Protocols {
    /// <summary>
    /// Менеджер рукопожатия
    /// Управляет процессом установления безопасного соединения
    /// Обрабатывает таймауты, повторные попытки и состояния
    /// </summary>
    public sealed class HandshakeManager : IDisposable {
        private readonly UdpTunnel _tunnel;
        private readonly NoiseProtocol _noise;
        private readonly Logger _logger;
        private CancellationTokenSource _cts;
        private Task _handshakeTask;

        public event Action OnHandshakeCompleted;
        public event Action<string> OnHandshakeFailed;
        public event Action<int> OnHandshakeProgress; // 0-100%

        public bool IsHandshakeComplete { get; private set; }
        public byte[] SessionKey { get; private set; }

        // Настройки
        private const int HandshakeTimeoutMs = 10000;
        private const int MaxRetries = 3;

        public HandshakeManager(UdpTunnel tunnel) {
            _tunnel = tunnel;
            _noise = new NoiseProtocol();
            _logger = Logger.Instance;
            _cts = new CancellationTokenSource();
        }

        /// <summary>
        /// Запуск рукопожатия как клиент
        /// </summary>
        public async Task<bool> StartAsClientAsync() {
            _logger.Info("Starting handshake as client");
            OnHandshakeProgress?.Invoke(10);

            try {
                var retryCount = 0;

                while (retryCount < MaxRetries && !IsHandshakeComplete) {
                    var success = await PerformClientHandshake(retryCount);

                    if (success) {
                        IsHandshakeComplete = true;
                        OnHandshakeCompleted?.Invoke();
                        _logger.Info("Handshake completed successfully");
                        return true;
                    }

                    retryCount++;

                    if (retryCount < MaxRetries) {
                        _logger.Warning($"Handshake failed, retrying ({retryCount}/{MaxRetries})");
                        await Task.Delay(1000 * retryCount);
                        OnHandshakeProgress?.Invoke(10 + (retryCount * 20));
                    }
                }

                OnHandshakeFailed?.Invoke("Handshake failed after max retries");
                return false;
            } catch (Exception ex) {
                _logger.Error($"Handshake error: {ex.Message}");
                OnHandshakeFailed?.Invoke(ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Запуск рукопожатия как сервер
        /// </summary>
        public async Task<bool> StartAsServerAsync() {
            _logger.Info("Starting handshake as server");
            OnHandshakeProgress?.Invoke(10);

            try {
                var cts = new CancellationTokenSource(HandshakeTimeoutMs);

                // Ждем сообщение от клиента
                var handshakeData = await WaitForHandshakeMessage(cts.Token);
                OnHandshakeProgress?.Invoke(30);

                // Отвечаем на рукопожатие
                var response = _noise.RespondToHandshake(handshakeData);
                await _tunnel.SendAsync(response);
                OnHandshakeProgress?.Invoke(70);

                SessionKey = _noise.GetSessionKey();
                IsHandshakeComplete = true;
                OnHandshakeCompleted?.Invoke();
                _logger.Info("Handshake completed successfully");

                return true;
            } catch (TimeoutException) {
                _logger.Error("Handshake timeout");
                OnHandshakeFailed?.Invoke("Handshake timeout");
                return false;
            } catch (Exception ex) {
                _logger.Error($"Handshake error: {ex.Message}");
                OnHandshakeFailed?.Invoke(ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Выполнение клиентской части рукопожатия
        /// </summary>
        private async Task<bool> PerformClientHandshake(int attempt) {
            try {
                // 1. Отправляем инициирующее сообщение
                var initMessage = _noise.InitiateHandshake();
                await _tunnel.SendAsync(initMessage);
                _logger.Debug("Initiation message sent");
                OnHandshakeProgress?.Invoke(20 + (attempt * 10));

                // 2. Ждем ответ
                var responseReceived = false;
                byte[] response = null;

                var cts = new CancellationTokenSource(HandshakeTimeoutMs);
                void Handler(byte[] data, System.Net.IPEndPoint endpoint) {
                    if (data.Length == 96) // Ответ от сервера
                    {
                        response = data;
                        responseReceived = true;
                        cts.Cancel();
                    }
                }

                _tunnel.OnPacketReceived += Handler;

                try {
                    await Task.Delay(HandshakeTimeoutMs, cts.Token);
                } catch (OperationCanceledException) {
                    // Ожидаемое исключение при получении ответа
                }

                _tunnel.OnPacketReceived -= Handler;

                if (!responseReceived || response == null) {
                    _logger.Warning("No handshake response received");
                    return false;
                }

                OnHandshakeProgress?.Invoke(60);

                // 3. Завершаем рукопожатие
                if (!_noise.FinalizeHandshake(response)) {
                    _logger.Warning("Handshake finalization failed");
                    return false;
                }

                SessionKey = _noise.GetSessionKey();
                OnHandshakeProgress?.Invoke(100);

                return true;
            } catch (Exception ex) {
                _logger.Error($"Client handshake error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Ожидание сообщения рукопожатия
        /// </summary>
        private async Task<byte[]> WaitForHandshakeMessage(CancellationToken token) {
            var tcs = new TaskCompletionSource<byte[]>();

            void Handler(byte[] data, System.Net.IPEndPoint endpoint) {
                if (data.Length == 128) // Сообщение от клиента
                {
                    tcs.TrySetResult(data);
                }
            }

            _tunnel.OnPacketReceived += Handler;

            using (token.Register(() => tcs.TrySetException(new TimeoutException()))) {
                try {
                    return await tcs.Task;
                } finally {
                    _tunnel.OnPacketReceived -= Handler;
                }
            }
        }

        /// <summary>
        /// Получение публичного ключа для отладки
        /// </summary>
        public byte[] GetRemotePublicKey() {
            return _noise.GetRemotePublicKey();
        }

        public void Dispose() {
            _cts?.Cancel();
            _cts?.Dispose();
            _noise?.Dispose();
        }
    }
}