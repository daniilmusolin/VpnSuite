using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace VpnCore.Cryptography {
    /// <summary>
    /// Perfect Forward Secrecy (PFS) - Совершенная прямая секретность
    /// Обеспечивает безопасность прошлых сессий при компрометации долговременного ключа
    /// Использует эфемерные (одноразовые) ключи для каждой сессии
    /// </summary>
    public sealed class PerfectForwardSecrecy : IDisposable {
        // Хранилище эфемерных ключей с их индексами
        private readonly ConcurrentDictionary<int, byte[]> _ephemeralKeys;
        private int _keyIndex;

        // Таймер для автоматической очистки старых ключей
        private readonly Timer _cleanupTimer;
        private readonly TimeSpan _keyLifetime = TimeSpan.FromMinutes(5);

        private bool _disposed;

        public PerfectForwardSecrecy() {
            _ephemeralKeys = new ConcurrentDictionary<int, byte[]>();
            _keyIndex = 0;

            // Запускаем таймер очистки каждые 5 минут
            _cleanupTimer = new Timer(CleanupExpiredKeys, null, _keyLifetime, _keyLifetime);
        }

        /// <summary>
        /// Генерация нового эфемерного ключа
        /// </summary>
        /// <returns>Эфемерный ключ (32 байта)</returns>
        public byte[] GenerateEphemeralKey() {
            var ephemeralKey = new byte[32];
            RandomNumberGenerator.Fill(ephemeralKey);

            var index = Interlocked.Increment(ref _keyIndex);
            _ephemeralKeys.TryAdd(index, ephemeralKey);

            return ephemeralKey;
        }

        /// <summary>
        /// Получение эфемерного ключа по индексу
        /// </summary>
        public byte[] GetEphemeralKey(int index) {
            return _ephemeralKeys.TryGetValue(index, out var key) ? key : null;
        }

        /// <summary>
        /// Инвалидация (удаление) эфемерного ключа
        /// </summary>
        public void InvalidateKey(int index) {
            if (_ephemeralKeys.TryRemove(index, out var key))
                CryptographicOperations.ZeroMemory(key);
        }

        /// <summary>
        /// Очистка устаревших ключей (вызывается таймером)
        /// </summary>
        private void CleanupExpiredKeys(object state) {
            // Удаляем ключи старше чем на 1000 индексов
            var cutoff = _keyIndex - 1000;
            foreach (var keyIndex in _ephemeralKeys.Keys) {
                if (keyIndex < cutoff)
                    InvalidateKey(keyIndex);
            }
        }

        /// <summary>
        /// Ротация ключа - создание нового ключа на основе старого
        /// Используется для периодической смены ключей в рамках сессии
        /// </summary>
        public byte[] RotateKey(byte[] currentKey) {
            using var sha512 = SHA512.Create();
            var newKey = new byte[32];
            var hash = sha512.ComputeHash(currentKey);

            // Берем первые 32 байта хеша как новый ключ
            Buffer.BlockCopy(hash, 0, newKey, 0, 32);

            // Безопасно удаляем старый ключ
            CryptographicOperations.ZeroMemory(currentKey);

            return newKey;
        }

        /// <summary>
        /// Вычисление ключа сессии с использованием HKDF
        /// </summary>
        public byte[] DeriveSessionKey(byte[] sharedSecret, byte[] salt) {
            using var hmac = new HMACSHA256(salt);
            return hmac.ComputeHash(sharedSecret);
        }

        /// <summary>
        /// Создание ключевой пары для новой сессии
        /// </summary>
        public (byte[] PrivateKey, byte[] PublicKey) GenerateSessionKeyPair() {
            using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            var parameters = ecdh.ExportParameters(true);

            var privateKey = parameters.D;
            var publicKey = new byte[64];
            Buffer.BlockCopy(parameters.Q.X, 0, publicKey, 0, 32);
            Buffer.BlockCopy(parameters.Q.Y, 0, publicKey, 32, 32);

            return (privateKey, publicKey);
        }

        public void CleanupKeys() {
            foreach (var key in _ephemeralKeys.Values)
                CryptographicOperations.ZeroMemory(key);

            _ephemeralKeys.Clear();
        }

        public void Dispose() {
            if (!_disposed) {
                _cleanupTimer?.Dispose();
                CleanupKeys();
                _disposed = true;
            }
        }
    }
}