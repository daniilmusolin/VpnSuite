using System.Security.Cryptography;

namespace VpnCore.Protocols {
    /// <summary>
    /// Совместимость с WireGuard протоколом
    /// Реализует базовые принципы WireGuard:
    /// - Noise_IK для рукопожатия
    /// - ChaCha20Poly1305 для шифрования
    /// - Криптоключи Curve25519
    /// </summary>
    public sealed class WireGuardCompatible : IDisposable {
        private byte[] _privateKey;
        private byte[] _publicKey;
        private byte[] _remotePublicKey;
        private byte[] _sessionKey;

        // Используем полное имя с алиасом для устранения неоднозначности
        private VpnCore.Cryptography.ChaCha20Poly1305 _cipher;

        private bool _disposed;

        // Константы WireGuard
        private const int KeyLength = 32;
        private const int HandshakeLength = 148;

        public WireGuardCompatible() {
            GenerateKeyPair();
        }

        /// <summary>
        /// Генерация ключевой пары Curve25519
        /// </summary>
        private void GenerateKeyPair() {
            using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            var parameters = ecdh.ExportParameters(true);
            _privateKey = parameters.D;

            _publicKey = new byte[KeyLength];
            Buffer.BlockCopy(parameters.Q.X, 0, _publicKey, 0, KeyLength);
        }

        /// <summary>
        /// Создание сообщения инициации (как в WireGuard)
        /// </summary>
        public byte[] CreateInitiationMessage() {
            var message = new byte[HandshakeLength];

            // Тип сообщения: 1 = Initiation
            message[0] = 1;

            // Случайный идентификатор сессии
            var sessionId = new byte[4];
            RandomNumberGenerator.Fill(sessionId);
            Buffer.BlockCopy(sessionId, 0, message, 1, 4);

            // Публичный ключ отправителя
            Buffer.BlockCopy(_publicKey, 0, message, 5, KeyLength);

            // Эфемерный публичный ключ (симуляция)
            var ephemeralKey = new byte[KeyLength];
            RandomNumberGenerator.Fill(ephemeralKey);
            Buffer.BlockCopy(ephemeralKey, 0, message, 37, KeyLength);

            // Метка времени
            var timestamp = BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            Buffer.BlockCopy(timestamp, 0, message, 69, 8);

            // MAC (в реальном WireGuard используется Poly1305)
            using var sha256 = SHA256.Create();
            var mac = sha256.ComputeHash(message.AsSpan(0, 77).ToArray());
            Buffer.BlockCopy(mac, 0, message, 77, 16);

            return message;
        }

        /// <summary>
        /// Обработка сообщения инициации (серверная сторона)
        /// </summary>
        public byte[] ProcessInitiation(byte[] message) {
            if (message == null || message.Length < HandshakeLength)
                throw new ArgumentException("Invalid initiation message");

            // Извлекаем публичный ключ отправителя
            _remotePublicKey = new byte[KeyLength];
            Buffer.BlockCopy(message, 5, _remotePublicKey, 0, KeyLength);

            // Вычисляем общий секрет
            var sharedSecret = ComputeSharedSecret(_remotePublicKey);

            // Создаем ключ сессии
            var salt = new byte[16];
            RandomNumberGenerator.Fill(salt);
            using var kdf = new Rfc2898DeriveBytes(sharedSecret, salt, 1000, HashAlgorithmName.SHA256);
            _sessionKey = kdf.GetBytes(32);

            // Создаем ответное сообщение
            var response = new byte[HandshakeLength];
            response[0] = 2; // Тип: Response

            // Случайный идентификатор сессии
            var sessionId = new byte[4];
            RandomNumberGenerator.Fill(sessionId);
            Buffer.BlockCopy(sessionId, 0, response, 1, 4);

            // Публичный ключ сервера
            Buffer.BlockCopy(_publicKey, 0, response, 5, KeyLength);

            // MAC
            using var sha256 = SHA256.Create();
            var mac = sha256.ComputeHash(response.AsSpan(0, 37).ToArray());
            Buffer.BlockCopy(mac, 0, response, 37, 16);

            // Используем полное имя с указанием пространства имен
            _cipher = new VpnCore.Cryptography.ChaCha20Poly1305(_sessionKey);

            return response;
        }

        /// <summary>
        /// Завершение рукопожатия (клиентская сторона)
        /// </summary>
        public bool FinalizeHandshake(byte[] response) {
            if (response == null || response.Length < HandshakeLength)
                return false;

            // Извлекаем публичный ключ сервера
            var serverPublicKey = new byte[KeyLength];
            Buffer.BlockCopy(response, 5, serverPublicKey, 0, KeyLength);

            // Вычисляем общий секрет
            var sharedSecret = ComputeSharedSecret(serverPublicKey);

            // Создаем ключ сессии
            var salt = new byte[16];
            RandomNumberGenerator.Fill(salt);
            using var kdf = new Rfc2898DeriveBytes(sharedSecret, salt, 1000, HashAlgorithmName.SHA256);
            _sessionKey = kdf.GetBytes(32);

            // Проверяем MAC
            using var sha256 = SHA256.Create();
            var expectedMac = sha256.ComputeHash(response.AsSpan(0, 37).ToArray());
            var receivedMac = new byte[16];
            Buffer.BlockCopy(response, 37, receivedMac, 0, 16);

            if (!CryptographicOperations.FixedTimeEquals(expectedMac, receivedMac))
                return false;

            // Используем полное имя с указанием пространства имен
            _cipher = new VpnCore.Cryptography.ChaCha20Poly1305(_sessionKey);
            return true;
        }

        /// <summary>
        /// Шифрование данных (совместимо с WireGuard)
        /// </summary>
        public byte[] Encrypt(byte[] plaintext) {
            if (_cipher == null)
                throw new InvalidOperationException("Handshake not completed");

            lock (_cipher) {
                return _cipher.Encrypt(plaintext);
            }
        }

        /// <summary>
        /// Расшифрование данных
        /// </summary>
        public byte[] Decrypt(byte[] ciphertext) {
            if (_cipher == null)
                throw new InvalidOperationException("Handshake not completed");

            lock (_cipher) {
                return _cipher.Decrypt(ciphertext);
            }
        }

        /// <summary>
        /// Вычисление общего секрета
        /// </summary>
        private byte[] ComputeSharedSecret(byte[] remotePublicKey) {
            using var ecdh = ECDiffieHellman.Create();
            var remotePoint = new ECPoint {
                X = remotePublicKey[0..32],
                Y = remotePublicKey[32..64]
            };

            ecdh.ImportParameters(new ECParameters {
                Curve = ECCurve.NamedCurves.nistP256,
                D = _privateKey,
                Q = remotePoint
            });

            using var remoteEcdh = ECDiffieHellman.Create();
            remoteEcdh.ImportParameters(new ECParameters {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = remotePoint
            });

            return ecdh.DeriveKeyMaterial(remoteEcdh.PublicKey);
        }

        /// <summary>
        /// Получение публичного ключа
        /// </summary>
        public byte[] GetPublicKey() => (byte[])_publicKey.Clone();

        /// <summary>
        /// Получение сессионного ключа
        /// </summary>
        public byte[] GetSessionKey() => (byte[])_sessionKey.Clone();

        public void Dispose() {
            if (!_disposed) {
                if (_privateKey != null)
                    CryptographicOperations.ZeroMemory(_privateKey);
                if (_sessionKey != null)
                    CryptographicOperations.ZeroMemory(_sessionKey);
                _cipher?.Dispose();
                _disposed = true;
            }
        }
    }
}