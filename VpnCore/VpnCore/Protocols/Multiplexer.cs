using System.Collections.Concurrent;
using VpnCore.Utils;

namespace VpnCore.Protocols {
    /// <summary>
    /// Мультиплексирование потоков через одно соединение
    /// Позволяет одновременно передавать данные от разных приложений
    /// Каждый поток имеет свой ID и последовательность
    /// </summary>
    public sealed class Multiplexer : IDisposable {
        private readonly ConcurrentDictionary<ushort, StreamContext> _streams;
        private ushort _nextStreamId;
        private readonly object _lock = new object();
        private CancellationTokenSource _cts;
        private readonly Logger _logger;
        private bool _disposed;

        // События
        public event Action<ushort, byte[]> OnStreamData;
        public event Action<ushort> OnStreamOpened;
        public event Action<ushort> OnStreamClosed;

        public int ActiveStreams => _streams.Count;

        public Multiplexer() {
            _streams = new ConcurrentDictionary<ushort, StreamContext>();
            _nextStreamId = 1;
            _cts = new CancellationTokenSource();
            _logger = Logger.Instance;
        }

        /// <summary>
        /// Открытие нового потока
        /// </summary>
        /// <returns>ID потока</returns>
        public ushort OpenStream() {
            lock (_lock) {
                var streamId = _nextStreamId++;
                var context = new StreamContext {
                    Id = streamId,
                    CreatedAt = DateTime.UtcNow,
                    Buffer = new BlockingCollection<byte[]>(),
                    IsOpen = true,
                    Statistics = new StreamStatistics()
                };

                _streams[streamId] = context;

                // Запускаем обработку потока
                _ = Task.Run(() => ProcessStream(streamId, _cts.Token));

                OnStreamOpened?.Invoke(streamId);
                _logger.Debug($"Stream {streamId} opened");

                return streamId;
            }
        }

        /// <summary>
        /// Отправка данных в поток
        /// </summary>
        public void SendData(ushort streamId, byte[] data) {
            if (!_streams.TryGetValue(streamId, out var stream) || !stream.IsOpen)
                throw new InvalidOperationException($"Stream {streamId} is not open");

            var packet = EncodeStreamPacket(streamId, data);
            stream.Statistics.BytesSent += data.Length;
            stream.Statistics.PacketsSent++;
            OnStreamData?.Invoke(streamId, packet);
        }

        /// <summary>
        /// Получение данных из сети и направление в соответствующий поток
        /// </summary>
        public void ReceiveData(byte[] packet) {
            var (streamId, data) = DecodeStreamPacket(packet);

            if (_streams.TryGetValue(streamId, out var stream) && stream.IsOpen) {
                stream.Buffer.Add(data);
                stream.Statistics.BytesReceived += data.Length;
                stream.Statistics.PacketsReceived++;
                stream.LastActivity = DateTime.UtcNow;
            } else {
                _logger.Warning($"Received data for unknown stream {streamId}");
            }
        }

        /// <summary>
        /// Закрытие потока
        /// </summary>
        public void CloseStream(ushort streamId) {
            if (_streams.TryRemove(streamId, out var stream)) {
                stream.IsOpen = false;
                stream.Buffer.CompleteAdding();
                OnStreamClosed?.Invoke(streamId);
                _logger.Debug($"Stream {streamId} closed");
            }
        }

        /// <summary>
        /// Получение статистики потока
        /// </summary>
        public StreamStatistics GetStreamStatistics(ushort streamId) {
            return _streams.TryGetValue(streamId, out var stream) ? stream.Statistics : null;
        }

        /// <summary>
        /// Обработка данных потока
        /// </summary>
        private async Task ProcessStream(ushort streamId, CancellationToken token) {
            if (!_streams.TryGetValue(streamId, out var stream))
                return;

            while (!token.IsCancellationRequested && stream.IsOpen) {
                try {
                    var data = stream.Buffer.Take(token);
                    await HandleStreamData(streamId, data);
                } catch (OperationCanceledException) {
                    break;
                } catch (InvalidOperationException) {
                    // Buffer was completed
                    break;
                } catch (Exception ex) {
                    _logger.Error($"Stream {streamId} processing error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Обработка данных потока (может быть переопределен)
        /// </summary>
        private Task HandleStreamData(ushort streamId, byte[] data) {
            // В реальном приложении здесь вызывается обработчик приложения
            _logger.Debug($"Stream {streamId} received {data.Length} bytes");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Кодирование пакета с информацией о потоке
        /// Формат: [StreamId (2)][DataLength (4)][Data (N)]
        /// </summary>
        private byte[] EncodeStreamPacket(ushort streamId, byte[] data) {
            var packet = new byte[2 + 4 + data.Length];
            BitConverter.GetBytes(streamId).CopyTo(packet, 0);
            BitConverter.GetBytes(data.Length).CopyTo(packet, 2);
            Buffer.BlockCopy(data, 0, packet, 6, data.Length);
            return packet;
        }

        /// <summary>
        /// Декодирование пакета
        /// </summary>
        private (ushort, byte[]) DecodeStreamPacket(byte[] packet) {
            if (packet.Length < 6)
                throw new ArgumentException("Packet too short");

            var streamId = BitConverter.ToUInt16(packet, 0);
            var dataLength = BitConverter.ToInt32(packet, 2);

            if (packet.Length < 6 + dataLength)
                throw new ArgumentException("Packet data truncated");

            var data = new byte[dataLength];
            Buffer.BlockCopy(packet, 6, data, 0, dataLength);

            return (streamId, data);
        }

        public void Dispose() {
            if (!_disposed) {
                _cts.Cancel();
                foreach (var stream in _streams.Values) {
                    stream.Buffer.CompleteAdding();
                }
                _streams.Clear();
                _cts.Dispose();
                _disposed = true;
            }
        }

        private class StreamContext {
            public ushort Id { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime LastActivity { get; set; }
            public BlockingCollection<byte[]> Buffer { get; set; }
            public bool IsOpen { get; set; }
            public StreamStatistics Statistics { get; set; }
        }
    }

    /// <summary>
    /// Статистика потока
    /// </summary>
    public class StreamStatistics {
        public long BytesSent { get; set; }
        public long BytesReceived { get; set; }
        public long PacketsSent { get; set; }
        public long PacketsReceived { get; set; }
        public DateTime CreatedAt { get; } = DateTime.UtcNow;

        public double SendSpeed => BytesSent / (DateTime.UtcNow - CreatedAt).TotalSeconds;
        public double ReceiveSpeed => BytesReceived / (DateTime.UtcNow - CreatedAt).TotalSeconds;
        public long TotalBytes => BytesSent + BytesReceived;
    }
}