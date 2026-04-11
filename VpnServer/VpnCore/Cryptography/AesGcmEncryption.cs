using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace VpnCore.Cryptography {
    /// <summary>
    /// Реализация AES-256-GCM (Galois/Counter Mode)
    /// Обеспечивает конфиденциальность (шифрование) и аутентификацию (проверку целостности)
    /// Использует аппаратное ускорение AES-NI на современных процессорах
    /// </summary>
    public sealed class AesGcmEncryption : IDisposable {
        // Константы алгоритма
        private const int KeySize = 32;           // 256 бит
        private const int TagSize = 16;           // 128 бит аутентификационный тег
        private const int NonceSize = 12;         // 96 бит (рекомендовано RFC 5116)
        private const int IvSize = 12;             // Вектор инициализации

        private readonly byte[] _key;              // Секретный ключ шифрования
        private readonly byte[] _salt;             // Соль для генерации nonce
        private ulong _counter;                    // Счетчик для предотвращения повторного использования nonce
        private readonly object _lock = new object(); // Синхронизация для многопоточности
        private bool _disposed;
        private readonly ArrayPool<byte> _bufferPool;

        /// <summary>
        /// Конструктор - инициализирует AES-256-GCM с заданным ключом
        /// </summary>
        /// <param name="preSharedKey">Предварительный общий ключ (может быть любого размера)</param>
        /// <param name="salt">Соль для KDF (опционально)</param>
        public AesGcmEncryption(byte[] preSharedKey, byte[] salt = null) {
            if (preSharedKey == null || preSharedKey.Length == 0)
                throw new ArgumentException("Key cannot be null or empty", nameof(preSharedKey));

            _bufferPool = ArrayPool<byte>.Shared;

            // PBKDF2 - ключевая функция для получения криптостойкого ключа
            // 210,000 итераций - рекомендация OWASP для 2024 года
            var actualSalt = salt ?? GenerateSecureSalt(KeySize);
            using var derive = new Rfc2898DeriveBytes(
                preSharedKey,
                actualSalt,
                210000,        // Итерации для защиты от брутфорса
                HashAlgorithmName.SHA512);

            _key = derive.GetBytes(KeySize);
            _salt = (byte[])actualSalt.Clone();
            _counter = (ulong)DateTime.UtcNow.Ticks; // Начальное значение счетчика
        }

        /// <summary>
        /// Генерация криптостойкой случайной соли
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private byte[] GenerateSecureSalt(int length) {
            var salt = new byte[length];
            RandomNumberGenerator.Fill(salt);
            return salt;
        }

        /// <summary>
        /// Шифрование данных с аутентификацией
        /// </summary>
        /// <param name="plaintext">Открытый текст для шифрования</param>
        /// <param name="associatedData">Дополнительные аутентифицируемые данные (AAD)</param>
        /// <returns>Зашифрованные данные с тегом аутентификации</returns>
        public byte[] Encrypt(byte[] plaintext, byte[] associatedData = null) {
            if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));
            if (plaintext.Length == 0) return Array.Empty<byte>();

            lock (_lock) // Блокировка для потокобезопасности
            {
                var nonce = GenerateNonce();          // Уникальный номер для этого шифрования
                var ciphertext = new byte[plaintext.Length];
                var tag = new byte[TagSize];

                using var aes = new AesGcm(_key, TagSize);
                aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

                // Формат: [Nonce(12)][Ciphertext(N)][Tag(16)]
                var result = new byte[NonceSize + ciphertext.Length + TagSize];
                Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
                Buffer.BlockCopy(ciphertext, 0, result, NonceSize, ciphertext.Length);
                Buffer.BlockCopy(tag, 0, result, NonceSize + ciphertext.Length, TagSize);

                _counter++; // Увеличиваем счетчик для следующего nonce
                return result;
            }
        }

        /// <summary>
        /// Расшифрование данных с проверкой аутентификации
        /// </summary>
        /// <param name="encrypted">Зашифрованные данные (включая nonce и tag)</param>
        /// <param name="associatedData">Дополнительные аутентифицируемые данные (должны совпадать с теми, что использовались при шифровании)</param>
        /// <returns>Расшифрованный открытый текст</returns>
        /// <exception cref="CryptographicException">Если аутентификация не пройдена (данные были изменены)</exception>
        public byte[] Decrypt(byte[] encrypted, byte[] associatedData = null) {
            if (encrypted == null) throw new ArgumentNullException(nameof(encrypted));
            if (encrypted.Length < NonceSize + TagSize)
                throw new ArgumentException($"Encrypted data too short. Minimum: {NonceSize + TagSize} bytes");

            lock (_lock) {
                // Извлекаем nonce, шифротекст и тег
                var nonce = new byte[NonceSize];
                var ciphertext = new byte[encrypted.Length - NonceSize - TagSize];
                var tag = new byte[TagSize];

                Buffer.BlockCopy(encrypted, 0, nonce, 0, NonceSize);
                Buffer.BlockCopy(encrypted, NonceSize, ciphertext, 0, ciphertext.Length);
                Buffer.BlockCopy(encrypted, NonceSize + ciphertext.Length, tag, 0, TagSize);

                var plaintext = new byte[ciphertext.Length];

                using var aes = new AesGcm(_key, TagSize);
                aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);

                return plaintext;
            }
        }

        /// <summary>
        /// Генерация уникального nonce (Number used ONCE)
        /// Комбинация соли и счетчика гарантирует уникальность
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private byte[] GenerateNonce() {
            var nonce = new byte[NonceSize];
            var counterBytes = BitConverter.GetBytes(_counter);

            // Первые 4 байта - соль (обеспечивает уникальность между разными экземплярами)
            Buffer.BlockCopy(_salt, 0, nonce, 0, 4);
            // Последние 8 байт - счетчик (уникальность в рамках одного экземпляра)
            Buffer.BlockCopy(counterBytes, 0, nonce, 4, 8);

            return nonce;
        }

        public void Dispose() {
            if (!_disposed) {
                // Безопасное удаление ключей из памяти
                CryptographicOperations.ZeroMemory(_key);
                CryptographicOperations.ZeroMemory(_salt);
                _disposed = true;
            }
        }
    }
}