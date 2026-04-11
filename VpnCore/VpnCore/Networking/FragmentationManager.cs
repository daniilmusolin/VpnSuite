using System.Collections.Concurrent;
using VpnCore.Models;
using VpnCore.Utils;

namespace VpnCore.Networking {
    /// <summary>
    /// Менеджер фрагментации
    /// Разбивает большие пакеты на маленькие фрагменты и собирает обратно
    /// Позволяет передавать данные больше MTU
    /// </summary>
    public sealed class FragmentationManager {
        private readonly ConcurrentDictionary<ushort, FragmentAssembly> _assemblies;
        private readonly Logger _logger;
        private ushort _nextFragmentId;
        private readonly object _lock = new object();

        // Максимальный размер фрагмента (обычно MTU - заголовок)
        private const int FragmentSize = 1400;

        // Таймаут сборки фрагментов (секунды)
        private const int AssemblyTimeoutSeconds = 10;

        public event Action<byte[]> OnCompleteMessageReceived;

        public FragmentationManager() {
            _assemblies = new ConcurrentDictionary<ushort, FragmentAssembly>();
            _logger = Logger.Instance;
            _nextFragmentId = 1;

            // Запускаем очистку старых сборок
            Task.Run(CleanupAssemblies);
        }

        /// <summary>
        /// Фрагментация большого сообщения
        /// </summary>
        public VpnPacket[] FragmentMessage(byte[] message, ushort streamId = 0) {
            var fragmentId = GetNextFragmentId();
            var totalFragments = (int)Math.Ceiling((double)message.Length / FragmentSize);
            var fragments = new VpnPacket[totalFragments];

            _logger.Debug($"Fragmenting message {message.Length} bytes into {totalFragments} fragments");

            for (int i = 0; i < totalFragments; i++) {
                var offset = i * FragmentSize;
                var length = Math.Min(FragmentSize, message.Length - offset);
                var fragmentData = new byte[length];
                Buffer.BlockCopy(message, offset, fragmentData, 0, length);

                var packet = new VpnPacket(PacketType.Fragment, fragmentData) {
                    FragmentId = fragmentId,
                    FragmentOffset = (ushort)offset,
                    TotalFragments = (ushort)totalFragments,
                    IsFragment = true,
                    StreamId = streamId
                };

                fragments[i] = packet;
            }

            return fragments;
        }

        /// <summary>
        /// Дефрагментация - сборка сообщения из фрагментов
        /// </summary>
        public bool Defragment(VpnPacket fragment, out byte[] completeMessage) {
            completeMessage = null;

            if (!fragment.IsFragment || fragment.Type != PacketType.Fragment)
                return false;

            var assembly = _assemblies.GetOrAdd(fragment.FragmentId, id => new FragmentAssembly {
                FragmentId = id,
                TotalFragments = fragment.TotalFragments,
                ReceivedFragments = new ConcurrentDictionary<ushort, byte[]>(),
                CreatedAt = DateTime.UtcNow
            });

            // Сохраняем фрагмент
            assembly.ReceivedFragments[fragment.FragmentOffset] = fragment.Data;
            _logger.Debug($"Fragment {fragment.FragmentId}:{fragment.FragmentOffset} received ({assembly.ReceivedFragments.Count}/{assembly.TotalFragments})");

            // Проверяем, собраны ли все фрагменты
            if (assembly.ReceivedFragments.Count == assembly.TotalFragments) {
                completeMessage = AssembleMessage(assembly);
                _assemblies.TryRemove(fragment.FragmentId, out _);
                _logger.Info($"Message assembled from {assembly.TotalFragments} fragments, size: {completeMessage.Length}");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Сборка сообщения из фрагментов
        /// </summary>
        private byte[] AssembleMessage(FragmentAssembly assembly) {
            var totalSize = 0;

            // Вычисляем общий размер
            foreach (var offset in assembly.ReceivedFragments.Keys) {
                totalSize += assembly.ReceivedFragments[offset].Length;
            }

            var message = new byte[totalSize];

            // Собираем в правильном порядке
            foreach (var kvp in assembly.ReceivedFragments.OrderBy(x => x.Key)) {
                Buffer.BlockCopy(kvp.Value, 0, message, kvp.Key, kvp.Value.Length);
            }

            return message;
        }

        /// <summary>
        /// Получение следующего ID фрагмента
        /// </summary>
        private ushort GetNextFragmentId() {
            lock (_lock) {
                var id = _nextFragmentId++;
                if (_nextFragmentId == ushort.MaxValue)
                    _nextFragmentId = 1;
                return id;
            }
        }

        /// <summary>
        /// Очистка устаревших сборок
        /// </summary>
        private async Task CleanupAssemblies() {
            while (true) {
                await Task.Delay(5000);

                var cutoff = DateTime.UtcNow.AddSeconds(-AssemblyTimeoutSeconds);
                foreach (var kvp in _assemblies) {
                    if (kvp.Value.CreatedAt < cutoff) {
                        _assemblies.TryRemove(kvp.Key, out _);
                        _logger.Warning($"Fragment assembly {kvp.Key} timed out");
                    }
                }
            }
        }

        private class FragmentAssembly {
            public ushort FragmentId { get; set; }
            public ushort TotalFragments { get; set; }
            public ConcurrentDictionary<ushort, byte[]> ReceivedFragments { get; set; }
            public DateTime CreatedAt { get; set; }
        }
    }
}