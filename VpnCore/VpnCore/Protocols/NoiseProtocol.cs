using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using VpnCore.Cryptography;

namespace VpnCore.Protocols {
    /// <summary>
    /// Паттерны рукопожатия Noise Protocol Framework
    /// </summary>
    public enum NoiseHandshakePattern {
        Noise_IK,   // Interactive key exchange (1 RTT)
        Noise_XX,   // Full handshake with identity hiding (2 RTT)
        Noise_NK    // Static key only (0 RTT)
    }

    /// <summary>
    /// Реализация Noise Protocol Framework
    /// Обеспечивает безопасное установление соединения с аутентификацией
    /// Использует комбинацию DH ключей и хеш-функций
    /// </summary>
    public sealed class NoiseProtocol : IDisposable {
        private readonly NoiseHandshakePattern _pattern;

        // Статические ключи (долговременные)
        private byte[] _localStaticPrivate;
        private byte[] _localStaticPublic;
        private byte[] _remoteStaticPublic;

        // Эфемерные ключи (одноразовые)
        private byte[] _ephemeralPrivate;
        private byte[] _ephemeralPublic;

        // Состояние рукопожатия
        private byte[] _handshakeHash;    // Хеш всей истории рукопожатия
        private byte[] _chainKey;         // Ключ для цепочки (KDF)
        private int _handshakeStage;
        private bool _isInitiator;

        private readonly PerfectForwardSecrecy _pfs;
        private bool _disposed;

        public NoiseProtocol(NoiseHandshakePattern pattern = NoiseHandshakePattern.Noise_IK) {
            _pattern = pattern;
            _pfs = new PerfectForwardSecrecy();
            GenerateStaticKeyPair();
            _handshakeStage = 0;
        }

        /// <summary>
        /// Генерация статической ключевой пары (долговременной)
        /// </summary>
        private void GenerateStaticKeyPair() {
            using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            var parameters = ecdh.ExportParameters(true);
            _localStaticPrivate = parameters.D;

            _localStaticPublic = new byte[64];
            Buffer.BlockCopy(parameters.Q.X, 0, _localStaticPublic, 0, 32);
            Buffer.BlockCopy(parameters.Q.Y, 0, _localStaticPublic, 32, 32);
        }

        /// <summary>
        /// Генерация эфемерной ключевой пары (одноразовой)
        /// </summary>
        private void GenerateEphemeralKeyPair() {
            using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            var parameters = ecdh.ExportParameters(true);
            _ephemeralPrivate = parameters.D;

            _ephemeralPublic = new byte[64];
            Buffer.BlockCopy(parameters.Q.X, 0, _ephemeralPublic, 0, 32);
            Buffer.BlockCopy(parameters.Q.Y, 0, _ephemeralPublic, 32, 32);
        }

        /// <summary>
        /// Инициация рукопожатия (вызывается клиентом)
        /// </summary>
        /// <returns>Сообщение для отправки серверу</returns>
        public byte[] InitiateHandshake() {
            _isInitiator = true;
            GenerateEphemeralKeyPair();

            // Вычисляем начальный хеш рукопожатия
            _handshakeHash = ComputeHandshakeHash(_ephemeralPublic);

            // Формат: [Static Public Key (64)][Ephemeral Public Key (64)]
            var handshakeMsg = new byte[128];
            Buffer.BlockCopy(_localStaticPublic, 0, handshakeMsg, 0, 64);
            Buffer.BlockCopy(_ephemeralPublic, 0, handshakeMsg, 64, 64);

            _handshakeStage = 1;
            return handshakeMsg;
        }

        /// <summary>
        /// Ответ на рукопожатие (вызывается сервером)
        /// </summary>
        /// <param name="handshakeData">Полученное сообщение от клиента</param>
        /// <returns>Ответное сообщение клиенту</returns>
        public byte[] RespondToHandshake(byte[] handshakeData) {
            _isInitiator = false;

            if (handshakeData.Length != 128)
                throw new ArgumentException("Invalid handshake data length");

            var remoteStatic = new byte[64];
            var remoteEphemeral = new byte[64];

            Buffer.BlockCopy(handshakeData, 0, remoteStatic, 0, 64);
            Buffer.BlockCopy(handshakeData, 64, remoteEphemeral, 0, 64);

            // Вычисляем DH между эфемерным и статическим ключами
            var dh1 = ComputeDhSharedSecret(_ephemeralPrivate, remoteStatic);
            var dh2 = ComputeDhSharedSecret(_localStaticPrivate, remoteEphemeral);

            // Микшируем ключи в цепочку
            _chainKey = MixKey(dh1);
            _chainKey = MixKey(dh2);

            // Генерируем свой эфемерный ключ для ответа
            GenerateEphemeralKeyPair();
            _handshakeHash = ComputeHandshakeHash(_ephemeralPublic);

            // Формат: [Ephemeral Public Key (64)][MAC (32)]
            var response = new byte[96];
            Buffer.BlockCopy(_ephemeralPublic, 0, response, 0, 64);

            var mac = ComputeMac(_chainKey, _ephemeralPublic);
            Buffer.BlockCopy(mac, 0, response, 64, 32);

            _remoteStaticPublic = remoteStatic;
            _handshakeStage = 2;

            return response;
        }

        /// <summary>
        /// Завершение рукопожатия (вызывается клиентом)
        /// </summary>
        /// <param name="response">Ответ от сервера</param>
        /// <returns>Успешно ли завершено рукопожатие</returns>
        public bool FinalizeHandshake(byte[] response) {
            if (response.Length != 96)
                return false;

            var remoteEphemeral = new byte[64];
            var receivedMac = new byte[32];

            Buffer.BlockCopy(response, 0, remoteEphemeral, 0, 64);
            Buffer.BlockCopy(response, 64, receivedMac, 0, 32);

            // Проверяем MAC (постоянное время)
            var expectedMac = ComputeMac(_chainKey, remoteEphemeral);

            if (!CryptographicOperations.FixedTimeEquals(expectedMac, receivedMac))
                return false;

            // Вычисляем финальный DH
            var dh3 = ComputeDhSharedSecret(_localStaticPrivate, remoteEphemeral);
            _chainKey = MixKey(dh3);

            _remoteStaticPublic = remoteEphemeral;
            _handshakeStage = 3;

            return true;
        }

        /// <summary>
        /// Вычисление DH общего секрета
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private byte[] ComputeDhSharedSecret(byte[] privateKey, byte[] publicKey) {
            using var ecdh = ECDiffieHellman.Create();
            var publicPoint = new ECPoint {
                X = publicKey[0..32],
                Y = publicKey[32..64]
            };

            ecdh.ImportParameters(new ECParameters {
                Curve = ECCurve.NamedCurves.nistP256,
                D = privateKey,
                Q = publicPoint
            });

            using var remoteEcdh = ECDiffieHellman.Create();
            remoteEcdh.ImportParameters(new ECParameters {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = publicPoint
            });

            return ecdh.DeriveKeyMaterial(remoteEcdh.PublicKey);
        }

        /// <summary>
        /// Вычисление хеша рукопожатия
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private byte[] ComputeHandshakeHash(byte[] data) {
            using var sha256 = SHA256.Create();
            return sha256.ComputeHash(data);
        }

        /// <summary>
        /// Вычисление MAC с использованием ключа цепочки
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private byte[] ComputeMac(byte[] key, byte[] data) {
            using var hmac = new HMACSHA256(key);
            return hmac.ComputeHash(data);
        }

        /// <summary>
        /// Микширование DH результата в ключ цепочки
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private byte[] MixKey(byte[] dh) {
            using var hmac = new HMACSHA256(_chainKey ?? new byte[32]);
            return hmac.ComputeHash(dh);
        }

        /// <summary>
        /// Получение финального ключа сессии
        /// </summary>
        public byte[] GetSessionKey() {
            if (_handshakeStage < 3)
                throw new InvalidOperationException("Handshake not completed");

            // Используем HKDF для получения ключа сессии из цепочки
            using var hkdf = new HKDF(HashAlgorithmName.SHA256);
            return hkdf.DeriveKey(_chainKey, 32, null, "VPN_SESSION_KEY");
        }

        /// <summary>
        /// Получение публичного ключа удаленной стороны
        /// </summary>
        public byte[] GetRemotePublicKey() => _remoteStaticPublic;

        public void Dispose() {
            if (!_disposed) {
                _pfs?.CleanupKeys();
                CryptographicOperations.ZeroMemory(_localStaticPrivate);
                CryptographicOperations.ZeroMemory(_ephemeralPrivate);
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// HKDF (HMAC-based Key Derivation Function) - RFC 5869
    /// </summary>
    internal class HKDF : IDisposable {
        private readonly HashAlgorithmName _hashAlgorithm;

        public HKDF(HashAlgorithmName hashAlgorithm) {
            _hashAlgorithm = hashAlgorithm;
        }

        public byte[] DeriveKey(byte[] inputKeyMaterial, int length, byte[] salt, string info) {
            var prk = Extract(salt ?? new byte[32], inputKeyMaterial);
            return Expand(prk, length, info);
        }

        private byte[] Extract(byte[] salt, byte[] inputKeyMaterial) {
            using var hmac = new HMACSHA256(salt);
            return hmac.ComputeHash(inputKeyMaterial);
        }

        private byte[] Expand(byte[] prk, int length, string info) {
            var output = new byte[length];
            var hashLength = 32;
            var iterations = (length + hashLength - 1) / hashLength;
            var temp = Array.Empty<byte>();
            var position = 0;
            var infoBytes = System.Text.Encoding.UTF8.GetBytes(info ?? "");

            for (int i = 1; i <= iterations; i++) {
                using var hmac = new HMACSHA256(prk);
                var input = new byte[temp.Length + infoBytes.Length + 1];
                Buffer.BlockCopy(temp, 0, input, 0, temp.Length);
                Buffer.BlockCopy(infoBytes, 0, input, temp.Length, infoBytes.Length);
                input[input.Length - 1] = (byte)i;

                temp = hmac.ComputeHash(input);
                var bytesToCopy = Math.Min(hashLength, length - position);
                Buffer.BlockCopy(temp, 0, output, position, bytesToCopy);
                position += bytesToCopy;
            }

            return output;
        }

        public void Dispose() { }
    }
}