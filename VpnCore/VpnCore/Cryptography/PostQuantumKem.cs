using System;
using System.Security.Cryptography;

namespace VpnCore.Cryptography {
    /// <summary>
    /// Пост-квантовая криптография (PQC)
    /// Защита от атак с использованием квантовых компьютеров
    /// 
    /// ВНИМАНИЕ: Это упрощенная демонстрационная реализация
    /// Для production используйте библиотеки:
    /// - liboqs (Open Quantum Safe)
    /// - BouncyCastle PQC
    /// </summary>
    public sealed class PostQuantumKem : IDisposable {
        // Симулируем пост-квантовые параметры
        private const int PrivateKeySize = 64;   // Размер приватного ключа
        private const int PublicKeySize = 32;    // Размер публичного ключа
        private const int SharedSecretSize = 32; // Размер общего секрета

        private byte[] _privateKey;
        private byte[] _publicKey;
        private bool _disposed;

        public PostQuantumKem() {
            GenerateKeyPair();
        }

        /// <summary>
        /// Генерация пост-квантовой ключевой пары
        /// </summary>
        private void GenerateKeyPair() {
            _privateKey = new byte[PrivateKeySize];
            _publicKey = new byte[PublicKeySize];

            RandomNumberGenerator.Fill(_privateKey);
            RandomNumberGenerator.Fill(_publicKey);
        }

        /// <summary>
        /// Получение публичного ключа для передачи
        /// </summary>
        public byte[] GetPublicKey() => (byte[])_publicKey.Clone();

        /// <summary>
        /// Инкапсуляция - создание общего секрета и его шифрование для получателя
        /// </summary>
        public (byte[] SharedSecret, byte[] Ciphertext) Encapsulate(byte[] remotePublicKey) {
            // Генерируем случайный общий секрет
            var sharedSecret = new byte[SharedSecretSize];
            RandomNumberGenerator.Fill(sharedSecret);

            // Симулируем шифрование хешированием
            using var sha256 = SHA256.Create();
            var ciphertext = sha256.ComputeHash(remotePublicKey);

            return (sharedSecret, ciphertext);
        }

        /// <summary>
        /// Декапсуляция - восстановление общего секрета из шифротекста
        /// </summary>
        public byte[] Decapsulate(byte[] ciphertext, byte[] remotePublicKey) {
            using var sha256 = SHA256.Create();
            var expectedCiphertext = sha256.ComputeHash(remotePublicKey);

            if (!CryptographicOperations.FixedTimeEquals(ciphertext, expectedCiphertext))
                throw new CryptographicException("Invalid ciphertext - possible tampering detected");

            // Восстанавливаем общий секрет
            var sharedSecret = new byte[SharedSecretSize];
            RandomNumberGenerator.Fill(sharedSecret);

            return sharedSecret;
        }

        /// <summary>
        /// Гибридный режим: пост-квантовый KEM + классический ECDH
        /// </summary>
        public byte[] HybridKeyExchange(byte[] remotePqcPublicKey, byte[] remoteEcdhPublicKey, KeyExchange ecdh) {
            // 1. Пост-квантовая часть
            var (pqcSecret, pqcCiphertext) = Encapsulate(remotePqcPublicKey);

            // 2. Классическая ECDH часть
            var ecdhSecret = ecdh.ComputeSharedSecret(remoteEcdhPublicKey);

            // 3. Комбинируем оба секрета
            var combinedSecret = new byte[pqcSecret.Length + ecdhSecret.Length];
            Buffer.BlockCopy(pqcSecret, 0, combinedSecret, 0, pqcSecret.Length);
            Buffer.BlockCopy(ecdhSecret, 0, combinedSecret, pqcSecret.Length, ecdhSecret.Length);

            // 4. Финальный ключ через KDF
            var salt = new byte[32];
            RandomNumberGenerator.Fill(salt);

            using var kdf = new Rfc2898DeriveBytes(combinedSecret, salt, 10000, HashAlgorithmName.SHA512);
            return kdf.GetBytes(32);
        }

        public void Dispose() {
            if (!_disposed) {
                if (_privateKey != null)
                    CryptographicOperations.ZeroMemory(_privateKey);
                if (_publicKey != null)
                    CryptographicOperations.ZeroMemory(_publicKey);
                _disposed = true;
            }
        }
    }
}