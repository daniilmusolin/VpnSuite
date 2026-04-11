using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace VpnCore.Cryptography {
    /// <summary>
    /// Пул криптографических объектов и буферов
    /// Уменьшает нагрузку на GC при интенсивной работе с криптографией
    /// Использует паттерн Singleton для глобального доступа
    /// </summary>
    public sealed class CryptoPool : IDisposable {
        private static CryptoPool _instance;
        private static readonly object _lock = new object();

        // Пулы различных типов объектов
        private readonly ConcurrentBag<byte[]> _keyPool;      // Пул ключей (32 байта)
        private readonly ConcurrentBag<byte[]> _bufferPool;   // Пул буферов (размер переменный)
        private readonly ConcurrentBag<AesGcmEncryption> _aesPool; // Пул AES шифраторов
        private readonly ConcurrentBag<ChaCha20Poly1305> _chachaPool; // Пул ChaCha20 шифраторов

        private readonly int _maxPoolSize = 50;   // Максимальный размер каждого пула
        private readonly int _defaultBufferSize = 4096; // Размер буфера по умолчанию

        private bool _disposed;

        /// <summary>
        /// Singleton instance
        /// </summary>
        public static CryptoPool Instance {
            get {
                if (_instance == null) {
                    lock (_lock) {
                        _instance ??= new CryptoPool();
                    }
                }
                return _instance;
            }
        }

        private CryptoPool() {
            _keyPool = new ConcurrentBag<byte[]>();
            _bufferPool = new ConcurrentBag<byte[]>();
            _aesPool = new ConcurrentBag<AesGcmEncryption>();
            _chachaPool = new ConcurrentBag<ChaCha20Poly1305>();
        }

        /// <summary>
        /// Получить ключ из пула или создать новый
        /// </summary>
        /// <param name="size">Размер ключа (обычно 32 байта)</param>
        public byte[] RentKey(int size = 32) {
            if (_keyPool.TryTake(out var key) && key.Length == size) {
                Array.Clear(key, 0, key.Length);
                return key;
            }
            return new byte[size];
        }

        /// <summary>
        /// Вернуть ключ в пул
        /// Ключ предварительно обнуляется для безопасности
        /// </summary>
        public void ReturnKey(byte[] key) {
            if (key == null) return;

            if (_keyPool.Count < _maxPoolSize) {
                CryptographicOperations.ZeroMemory(key);
                _keyPool.Add(key);
            }
        }

        /// <summary>
        /// Получить буфер из пула
        /// </summary>
        public byte[] RentBuffer(int minimumSize) {
            if (_bufferPool.TryTake(out var buffer) && buffer.Length >= minimumSize)
                return buffer;

            // Размер должен быть степенью двойки для лучшей аллокации
            var size = 1 << (int)Math.Ceiling(Math.Log2(minimumSize));
            return new byte[Math.Max(size, _defaultBufferSize)];
        }

        /// <summary>
        /// Вернуть буфер в пул
        /// </summary>
        public void ReturnBuffer(byte[] buffer) {
            if (buffer == null) return;

            if (_bufferPool.Count < _maxPoolSize) {
                Array.Clear(buffer, 0, buffer.Length);
                _bufferPool.Add(buffer);
            }
        }

        /// <summary>
        /// Получить AES шифратор из пула
        /// </summary>
        public AesGcmEncryption RentAes(byte[] key) {
            if (_aesPool.TryTake(out var aes)) {
                // В реальном проекте нужно переинициализировать с новым ключом
                return aes;
            }
            return new AesGcmEncryption(key);
        }

        /// <summary>
        /// Вернуть AES шифратор в пул
        /// </summary>
        public void ReturnAes(AesGcmEncryption aes) {
            if (aes != null && _aesPool.Count < _maxPoolSize)
                _aesPool.Add(aes);
        }

        /// <summary>
        /// Получить ChaCha20 шифратор из пула
        /// </summary>
        public ChaCha20Poly1305 RentChaCha(byte[] key) {
            if (_chachaPool.TryTake(out var chacha))
                return chacha;
            return new ChaCha20Poly1305(key);
        }

        /// <summary>
        /// Вернуть ChaCha20 шифратор в пул
        /// </summary>
        public void ReturnChaCha(ChaCha20Poly1305 chacha) {
            if (chacha != null && _chachaPool.Count < _maxPoolSize)
                _chachaPool.Add(chacha);
        }

        /// <summary>
        /// Очистка всех пулов
        /// </summary>
        public void Clear() {
            foreach (var key in _keyPool)
                CryptographicOperations.ZeroMemory(key);

            _keyPool.Clear();
            _bufferPool.Clear();
            _aesPool.Clear();
            _chachaPool.Clear();
        }

        public void Dispose() {
            if (!_disposed) {
                Clear();
                _disposed = true;
            }
        }
    }
}