using System.Collections.Concurrent;
using VpnCore.Utils;

namespace VpnCore.Networking {
    /// <summary>
    /// Эмуляция TCP поверх UDP
    /// Добавляет надежность: подтверждения, переотправку, контроль порядка
    /// </summary>
    public sealed class TcpOverUdp : IDisposable {
        private readonly UdpTunnel _tunnel;
        private readonly ConcurrentDictionary<uint, PendingPacket> _pendingPackets;
        private readonly ConcurrentDictionary<uint, DateTime> _receivedPackets;
        private readonly Logger _logger;
        private readonly Timer _retransmitTimer;
        private uint _nextSequenceNumber;
        private uint _expectedSequenceNumber;
        private bool _disposed;

        // Настройки
        private const int RetransmitTimeoutMs = 1000;
        private const int MaxRetransmits = 5;
        private const int WindowSize = 32;

        public event Action<byte[]> OnReliableDataReceived;

        public TcpOverUdp(UdpTunnel tunnel) {
            _tunnel = tunnel;
            _pendingPackets = new ConcurrentDictionary<uint, PendingPacket>();
            _receivedPackets = new ConcurrentDictionary<uint, DateTime>();
            _logger = Logger.Instance;
            _retransmitTimer = new Timer(CheckRetransmits, null, 100, 100);
            _nextSequenceNumber = 1;
            _expectedSequenceNumber = 1;

            // Подписываемся на события туннеля
            _tunnel.OnPacketReceived += HandleRawPacket;
        }

        /// <summary>
        /// Отправка надежных данных
        /// </summary>
        public async Task SendReliableAsync(byte[] data) {
            var sequenceNumber = _nextSequenceNumber++;
            var packet = CreateReliablePacket(sequenceNumber, data);

            var pending = new PendingPacket {
                SequenceNumber = sequenceNumber,
                Data = packet,
                SentAt = DateTime.UtcNow,
                RetransmitCount = 0
            };

            _pendingPackets[sequenceNumber] = pending;
            await _tunnel.SendAsync(packet);
            _logger.Debug($"Sent reliable packet #{sequenceNumber}, size: {data.Length}");
        }

        /// <summary>
        /// Создание надежного пакета с заголовком
        /// </summary>
        private byte[] CreateReliablePacket(uint sequenceNumber, byte[] data) {
            var header = new byte[4]; // 4 байта для sequence number
            BitConverter.GetBytes(sequenceNumber).CopyTo(header, 0);

            var packet = new byte[header.Length + data.Length];
            Buffer.BlockCopy(header, 0, packet, 0, header.Length);
            Buffer.BlockCopy(data, 0, packet, header.Length, data.Length);

            return packet;
        }

        /// <summary>
        /// Обработка полученного пакета
        /// </summary>
        private void HandleRawPacket(byte[] data, System.Net.IPEndPoint endpoint) {
            if (data.Length < 4) return;

            var sequenceNumber = BitConverter.ToUInt32(data, 0);
            var payload = new byte[data.Length - 4];
            Buffer.BlockCopy(data, 4, payload, 0, payload.Length);

            // Отправляем подтверждение
            SendAck(sequenceNumber);

            // Проверяем дубликаты
            if (_receivedPackets.ContainsKey(sequenceNumber)) {
                _logger.Debug($"Duplicate packet #{sequenceNumber} ignored");
                return;
            }

            _receivedPackets[sequenceNumber] = DateTime.UtcNow;
            CleanupOldReceivedPackets();

            // Проверяем порядок
            if (sequenceNumber == _expectedSequenceNumber) {
                OnReliableDataReceived?.Invoke(payload);
                _expectedSequenceNumber++;

                // Проверяем, нет ли следующих пакетов в буфере
                CheckPendingSequences();
            } else if (sequenceNumber > _expectedSequenceNumber) {
                // Пакет пришел раньше времени - сохраняем в буфер
                _logger.Debug($"Out-of-order packet #{sequenceNumber}, expecting #{_expectedSequenceNumber}");
                // TODO: Буферизация out-of-order пакетов
            }
        }

        /// <summary>
        /// Отправка подтверждения
        /// </summary>
        private async void SendAck(uint sequenceNumber) {
            var ackPacket = new byte[5];
            ackPacket[0] = 0x01; // ACK флаг
            BitConverter.GetBytes(sequenceNumber).CopyTo(ackPacket, 1);
            await _tunnel.SendAsync(ackPacket);
        }

        /// <summary>
        /// Проверка буферизованных последовательностей
        /// </summary>
        private void CheckPendingSequences() {
            // TODO: Проверка буфера ожидающих пакетов
        }

        /// <summary>
        /// Проверка необходимости переотправки
        /// </summary>
        private void CheckRetransmits(object state) {
            var now = DateTime.UtcNow;

            foreach (var pending in _pendingPackets.Values) {
                var elapsed = now - pending.SentAt;

                if (elapsed.TotalMilliseconds > RetransmitTimeoutMs) {
                    if (pending.RetransmitCount >= MaxRetransmits) {
                        _logger.Error($"Max retransmits reached for packet #{pending.SequenceNumber}");
                        _pendingPackets.TryRemove(pending.SequenceNumber, out _);
                        continue;
                    }

                    pending.RetransmitCount++;
                    pending.SentAt = now;
                    _tunnel.SendAsync(pending.Data).Wait(100);
                    _logger.Warning($"Retransmitting packet #{pending.SequenceNumber}, attempt {pending.RetransmitCount}");
                }
            }
        }

        /// <summary>
        /// Очистка старых записей о полученных пакетах
        /// </summary>
        private void CleanupOldReceivedPackets() {
            var cutoff = DateTime.UtcNow.AddMinutes(-5);
            foreach (var kvp in _receivedPackets) {
                if (kvp.Value < cutoff)
                    _receivedPackets.TryRemove(kvp.Key, out _);
            }
        }

        private class PendingPacket {
            public uint SequenceNumber { get; set; }
            public byte[] Data { get; set; }
            public DateTime SentAt { get; set; }
            public int RetransmitCount { get; set; }
        }

        public void Dispose() {
            if (!_disposed) {
                _retransmitTimer?.Dispose();
                _tunnel.OnPacketReceived -= HandleRawPacket;
                _disposed = true;
            }
        }
    }
}