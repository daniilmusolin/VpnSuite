using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace VpnCore.Utils {
    /// <summary>
    /// Пул буферов для уменьшения нагрузки на GC
    /// Предотвращает частые аллокации и деаллокации памяти
    /// Использует паттерн пул объектов
    /// </summary>
    public sealed class BufferPool : IDisposable {
        private static BufferPool _instance;
        private static readonly object _lock = new object();

        private readonly ConcurrentBag<byte[]>[] _buffersBySize;
        private readonly int[] _sizes;
        private readonly int _maxPoolSizePerSize;
        private bool _disposed;

        /// <summary>
        /// Предопределенные размеры буферов (степени двойки)
        /// </summary>
        private static readonly int[] DefaultSizes =
        {
            256,    // 256 байт
            512,    // 512 байт
            1024,   // 1 KB
            2048,   // 2 KB
            4096,   // 4 KB
            8192,   // 8 KB
            16384,  // 16 KB
            32768,  // 32 KB
            65536   // 64 KB
        };

        public static BufferPool Instance {
            get {
                if (_instance == null) {
                    lock (_lock) {
                        _instance ??= new BufferPool();
                    }
                }
                return _instance;
            }
        }

        private BufferPool(int maxPoolSizePerSize = 50) {
            _maxPoolSizePerSize = maxPoolSizePerSize;
            _sizes = DefaultSizes;
            _buffersBySize = new ConcurrentBag<byte[]>[_sizes.Length];

            for (int i = 0; i < _sizes.Length; i++) {
                _buffersBySize[i] = new ConcurrentBag<byte[]>();
            }
        }

        /// <summary>
        /// Получение буфера из пула
        /// </summary>
        /// <param name="minimumSize">Минимальный требуемый размер</param>
        /// <returns>Буфер (может быть больше запрошенного размера)</returns>
        public byte[] Rent(int minimumSize) {
            var sizeIndex = GetSizeIndex(minimumSize);

            if (sizeIndex >= 0 && _buffersBySize[sizeIndex].TryTake(out var buffer)) {
                return buffer;
            }

            // Создаем новый буфер
            var actualSize = sizeIndex >= 0 ? _sizes[sizeIndex] : minimumSize;
            return new byte[actualSize];
        }

        /// <summary>
        /// Возврат буфера в пул
        /// </summary>
        public void Return(byte[] buffer) {
            if (buffer == null) return;

            var sizeIndex = GetSizeIndex(buffer.Length);
            if (sizeIndex >= 0 && _buffersBySize[sizeIndex].Count < _maxPoolSizePerSize) {
                // Очищаем буфер перед возвратом
                Array.Clear(buffer, 0, buffer.Length);
                _buffersBySize[sizeIndex].Add(buffer);
            }
        }

        /// <summary>
        /// Получение индекса размера для заданного минимального размера
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetSizeIndex(int minimumSize) {
            for (int i = 0; i < _sizes.Length; i++) {
                if (_sizes[i] >= minimumSize)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Получение оптимального размера буфера
        /// </summary>
        public int GetOptimalSize(int requestedSize) {
            var index = GetSizeIndex(requestedSize);
            return index >= 0 ? _sizes[index] : requestedSize;
        }

        /// <summary>
        /// Очистка пула
        /// </summary>
        public void Clear() {
            foreach (var bag in _buffersBySize) {
                while (bag.TryTake(out _)) { }
            }
        }

        /// <summary>
        /// Получение статистики пула
        /// </summary>
        public PoolStatistics GetStatistics() {
            var stats = new PoolStatistics();
            for (int i = 0; i < _sizes.Length; i++) {
                stats.AddEntry(_sizes[i], _buffersBySize[i].Count);
            }
            return stats;
        }

        public void Dispose() {
            if (!_disposed) {
                Clear();
                _disposed = true;
            }
        }
    }

    public class PoolStatistics {
        public List<PoolSizeEntry> Entries { get; } = new List<PoolSizeEntry>();

        public void AddEntry(int size, int count) {
            Entries.Add(new PoolSizeEntry { Size = size, Count = count });
        }

        public int TotalBuffers => Entries.Sum(e => e.Count);
        public long TotalMemory => Entries.Sum(e => (long)e.Size * e.Count);
    }

    public class PoolSizeEntry {
        public int Size { get; set; }
        public int Count { get; set; }
    }
}