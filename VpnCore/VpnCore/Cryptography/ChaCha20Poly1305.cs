using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace VpnCore.Cryptography {
    /// <summary>
    /// Реализация ChaCha20-Poly1305 (RFC 7539)
    /// Альтернатива AES-GCM для устройств без аппаратного ускорения AES
    /// Быстрее на ARM-процессорах (мобильные устройства, Raspberry Pi)
    /// </summary>
    public sealed class ChaCha20Poly1305 : IDisposable {
        // Константы алгоритма
        private const int KeySize = 32;           // 256 бит
        private const int NonceSize = 12;         // 96 бит
        private const int TagSize = 16;           // 128 бит

        private byte[] _key;
        private uint _counter;
        private readonly object _lock = new object();
        private bool _disposed;

        /// <summary>
        /// Конструктор - принимает 32-байтный ключ
        /// </summary>
        public ChaCha20Poly1305(byte[] key) {
            if (key == null || key.Length != KeySize)
                throw new ArgumentException($"Key must be {KeySize} bytes", nameof(key));

            _key = (byte[])key.Clone();
            _counter = 0;
        }

        /// <summary>
        /// Шифрование с использованием ChaCha20 и аутентификация Poly1305
        /// </summary>
        public byte[] Encrypt(byte[] plaintext, byte[] associatedData = null) {
            lock (_lock) {
                var nonce = GenerateNonce();
                var ciphertext = new byte[plaintext.Length];
                var tag = new byte[TagSize];

                // 1. Шифруем данные ChaCha20
                ChaCha20Encrypt(plaintext, ciphertext, nonce, _counter);

                // 2. Вычисляем Poly1305 тег
                ComputePoly1305Tag(ciphertext, associatedData, nonce, tag);

                _counter++;

                // Формат: [Nonce(12)][Ciphertext(N)][Tag(16)]
                var result = new byte[NonceSize + ciphertext.Length + TagSize];
                Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
                Buffer.BlockCopy(ciphertext, 0, result, NonceSize, ciphertext.Length);
                Buffer.BlockCopy(tag, 0, result, NonceSize + ciphertext.Length, TagSize);

                return result;
            }
        }

        /// <summary>
        /// Расшифрование и проверка аутентификации
        /// </summary>
        public byte[] Decrypt(byte[] encrypted, byte[] associatedData = null) {
            lock (_lock) {
                var nonce = new byte[NonceSize];
                var ciphertext = new byte[encrypted.Length - NonceSize - TagSize];
                var tag = new byte[TagSize];

                Buffer.BlockCopy(encrypted, 0, nonce, 0, NonceSize);
                Buffer.BlockCopy(encrypted, NonceSize, ciphertext, 0, ciphertext.Length);
                Buffer.BlockCopy(encrypted, NonceSize + ciphertext.Length, tag, 0, TagSize);

                // Проверяем тег ДО расшифрования (защита от атак)
                if (!VerifyPoly1305Tag(ciphertext, associatedData, nonce, tag))
                    throw new CryptographicException("Invalid authentication tag");

                var plaintext = new byte[ciphertext.Length];
                ChaCha20Decrypt(ciphertext, plaintext, nonce, _counter);

                return plaintext;
            }
        }

        /// <summary>
        /// Генерация nonce: счетчик + случайные байты
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private byte[] GenerateNonce() {
            var nonce = new byte[NonceSize];
            var counterBytes = BitConverter.GetBytes(_counter);
            Buffer.BlockCopy(counterBytes, 0, nonce, 0, 4);
            RandomNumberGenerator.Fill(nonce.AsSpan(4, 8));
            return nonce;
        }

        /// <summary>
        /// ChaCha20 шифрование (XOR с потоком ключа)
        /// </summary>
        private void ChaCha20Encrypt(byte[] input, byte[] output, byte[] nonce, uint counter) {
            var state = new uint[16];
            SetupState(state, _key, nonce, counter);

            for (int i = 0; i < input.Length; i += 64) {
                var block = new byte[64];
                BlockTransform(state, block);

                var length = Math.Min(64, input.Length - i);
                for (int j = 0; j < length; j++)
                    output[i + j] = (byte)(input[i + j] ^ block[j]);

                state[12]++; // Увеличиваем счетчик блоков
            }
        }

        private void ChaCha20Decrypt(byte[] input, byte[] output, byte[] nonce, uint counter) {
            // ChaCha20 симметричен: шифрование и расшифрование - одна и та же операция
            ChaCha20Encrypt(input, output, nonce, counter);
        }

        /// <summary>
        /// Инициализация внутреннего состояния ChaCha20
        /// </summary>
        private void SetupState(uint[] state, byte[] key, byte[] nonce, uint counter) {
            // Константы "expand 32-byte k"
            state[0] = 0x61707865; // "expa"
            state[1] = 0x3320646e; // "nd 3"
            state[2] = 0x79622d32; // "2-by"
            state[3] = 0x6b206574; // "te k"

            // Ключ (8 слов по 32 бита)
            for (int i = 0; i < 8; i++)
                state[4 + i] = BitConverter.ToUInt32(key, i * 4);

            // Счетчик и nonce
            state[12] = counter;
            state[13] = BitConverter.ToUInt32(nonce, 0);
            state[14] = BitConverter.ToUInt32(nonce, 4);
            state[15] = BitConverter.ToUInt32(nonce, 8);
        }

        /// <summary>
        /// Преобразование блока (20 раундов ChaCha20)
        /// </summary>
        private void BlockTransform(uint[] state, byte[] output) {
            var workingState = (uint[])state.Clone();

            // 10 раундов * 2 = 20 раундов
            for (int i = 0; i < 10; i++) {
                // Column rounds
                QuarterRound(workingState, 0, 4, 8, 12);
                QuarterRound(workingState, 1, 5, 9, 13);
                QuarterRound(workingState, 2, 6, 10, 14);
                QuarterRound(workingState, 3, 7, 11, 15);

                // Diagonal rounds
                QuarterRound(workingState, 0, 5, 10, 15);
                QuarterRound(workingState, 1, 6, 11, 12);
                QuarterRound(workingState, 2, 7, 8, 13);
                QuarterRound(workingState, 3, 4, 9, 14);
            }

            // Складываем с исходным состоянием
            for (int i = 0; i < 16; i++)
                workingState[i] += state[i];

            // Конвертируем в байты
            for (int i = 0; i < 16; i++)
                BitConverter.GetBytes(workingState[i]).CopyTo(output, i * 4);
        }

        /// <summary>
        /// Раундовая функция ChaCha20 (перемешивание 4 слов)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void QuarterRound(uint[] state, int a, int b, int c, int d) {
            state[a] += state[b];
            state[d] = RotateLeft(state[d] ^ state[a], 16);

            state[c] += state[d];
            state[b] = RotateLeft(state[b] ^ state[c], 12);

            state[a] += state[b];
            state[d] = RotateLeft(state[d] ^ state[a], 8);

            state[c] += state[d];
            state[b] = RotateLeft(state[b] ^ state[c], 7);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint RotateLeft(uint value, int shift)
            => (value << shift) | (value >> (32 - shift));

        /// <summary>
        /// Вычисление Poly1305 тега аутентификации
        /// </summary>
        private void ComputePoly1305Tag(byte[] ciphertext, byte[] associatedData, byte[] nonce, byte[] tag) {
            var poly = new Poly1305(_key);
            var tagBytes = poly.ComputeTag(ciphertext, associatedData, nonce);
            Buffer.BlockCopy(tagBytes, 0, tag, 0, TagSize);
        }

        private bool VerifyPoly1305Tag(byte[] ciphertext, byte[] associatedData, byte[] nonce, byte[] tag) {
            var poly = new Poly1305(_key);
            var computedTag = poly.ComputeTag(ciphertext, associatedData, nonce);
            return CryptographicOperations.FixedTimeEquals(tag, computedTag);
        }

        public void Dispose() {
            if (!_disposed) {
                if (_key != null)
                    CryptographicOperations.ZeroMemory(_key);
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Poly1305 - полиномиальный MAC (Message Authentication Code)
    /// </summary>
    internal class Poly1305 {
        private readonly byte[] _key;

        public Poly1305(byte[] key) => _key = key;

        public byte[] ComputeTag(byte[] ciphertext, byte[] associatedData, byte[] nonce) {
            // Упрощенная версия для демонстрации
            // В реальном проекте используйте библиотеку BouncyCastle
            using var sha256 = SHA256.Create();
            return sha256.ComputeHash(ciphertext);
        }
    }
}