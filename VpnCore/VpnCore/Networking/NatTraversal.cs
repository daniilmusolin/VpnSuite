using System.Net;
using System.Net.Sockets;
using VpnCore.Utils;

namespace VpnCore.Networking {
    /// <summary>
    /// Тип NAT (Network Address Translation)
    /// </summary>
    public enum NatType {
        Unknown,
        OpenInternet,      // Прямое соединение без NAT
        FullCone,          // Full Cone NAT
        RestrictedCone,    // Restricted Cone NAT
        PortRestrictedCone,// Port Restricted Cone NAT
        Symmetric          // Symmetric NAT (самый сложный)
    }

    /// <summary>
    /// Обход NAT (Network Address Translation)
    /// Позволяет устанавливать соединения между клиентами за NAT
    /// Использует технику UDP hole punching
    /// </summary>
    public sealed class NatTraversal : IDisposable {
        private UdpClient _udpClient;
        private readonly Logger _logger;
        private CancellationTokenSource _cts;
        private NatType _natType;

        public NatType DetectedNatType => _natType;

        public NatTraversal() {
            _logger = Logger.Instance;
            _cts = new CancellationTokenSource();
        }

        /// <summary>
        /// Определение типа NAT с помощью STUN (RFC 3489)
        /// </summary>
        public async Task<NatType> DetectNatTypeAsync(string stunServer = "stun.l.google.com", int stunPort = 19302) {
            _logger.Info("Detecting NAT type...");

            try {
                _udpClient = new UdpClient();
                _udpClient.Connect(stunServer, stunPort);

                // Отправляем STUN запрос
                var stunRequest = CreateStunRequest();
                await _udpClient.SendAsync(stunRequest, stunRequest.Length);

                // Ждем ответ
                var result = await _udpClient.ReceiveAsync();
                _natType = ParseStunResponse(result.Buffer);

                _logger.Info($"NAT type detected: {_natType}");
                return _natType;
            } catch (Exception ex) {
                _logger.Error($"NAT detection failed: {ex.Message}");
                _natType = NatType.Unknown;
                return NatType.Unknown;
            }
        }

        /// <summary>
        /// UDP hole punching - установка прямого соединения через NAT
        /// </summary>
        public async Task<bool> HolePunchAsync(IPEndPoint targetEndpoint, int punchCount = 5) {
            _logger.Info($"Starting UDP hole punch to {targetEndpoint}");

            try {
                var punchTasks = new Task[punchCount];

                for (int i = 0; i < punchCount; i++) {
                    var delay = i * 100; // Увеличиваем задержку между попытками
                    punchTasks[i] = SendPunchPacketAsync(targetEndpoint, delay);
                }

                await Task.WhenAll(punchTasks);

                // Ждем ответ в течение 5 секунд
                var cts = new CancellationTokenSource(5000);
                var response = await _udpClient.ReceiveAsync(cts.Token);

                _logger.Info("Hole punch successful!");
                return true;
            } catch (TimeoutException) {
                _logger.Warning("Hole punch timeout - no response");
                return false;
            } catch (Exception ex) {
                _logger.Error($"Hole punch failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Отправка "пробивающих" пакетов
        /// </summary>
        private async Task SendPunchPacketAsync(IPEndPoint target, int delayMs) {
            await Task.Delay(delayMs);
            var punchPacket = CreatePunchPacket();
            await _udpClient.SendAsync(punchPacket, punchPacket.Length, target);
            _logger.Debug($"Punch packet sent to {target}");
        }

        /// <summary>
        /// Создание STUN запроса
        /// </summary>
        private byte[] CreateStunRequest() {
            // STUN заголовок: 20 байт
            var request = new byte[20];

            // Тип сообщения: Binding Request (0x0001)
            request[0] = 0x00;
            request[1] = 0x01;

            // Длина сообщения (0)
            request[2] = 0x00;
            request[3] = 0x00;

            // Transaction ID (16 случайных байт)
            var transactionId = new byte[16];
            Random.Shared.NextBytes(transactionId);
            Buffer.BlockCopy(transactionId, 0, request, 4, 16);

            return request;
        }

        /// <summary>
        /// Парсинг STUN ответа
        /// </summary>
        private NatType ParseStunResponse(byte[] response) {
            // Упрощенный парсинг
            // В реальном проекте нужно анализировать атрибуты XOR-MAPPED-ADDRESS

            if (response.Length < 20)
                return NatType.Unknown;

            // Проверяем наличие атрибута XOR-MAPPED-ADDRESS
            for (int i = 20; i < response.Length - 4; i++) {
                if (response[i] == 0x00 && response[i + 1] == 0x20) {
                    // Это Symmetric NAT? Сложно определить без второго сервера
                    return NatType.PortRestrictedCone;
                }
            }

            return NatType.FullCone;
        }

        /// <summary>
        /// Создание пакета для hole punching
        /// </summary>
        private byte[] CreatePunchPacket() {
            var packet = new byte[12];
            var magic = BitConverter.GetBytes(0xFEEDFACE);
            Buffer.BlockCopy(magic, 0, packet, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(DateTime.UtcNow.Ticks), 0, packet, 4, 8);
            return packet;
        }

        public void Dispose() {
            _cts?.Cancel();
            _udpClient?.Dispose();
            _cts?.Dispose();
        }
    }
}