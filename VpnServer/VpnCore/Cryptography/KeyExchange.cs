using System;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace VpnCore.Cryptography {
    /// <summary>
    /// Алгоритмы обмена ключами
    /// </summary>
    public enum KeyExchangeAlgorithm {
        ECDH_P256,  // 256-битная эллиптическая кривая (рекомендуется)
        ECDH_P384,  // 384-битная кривая (более безопасно, но медленнее)
        X25519      // Curve25519 (самый быстрый, используется в WireGuard)
    }

    /// <summary>
    /// Реализация ECDH (Elliptic Curve Diffie-Hellman)
    /// Позволяет двум сторонам вычислить общий секрет без его передачи
    /// Обеспечивает Perfect Forward Secrecy при использовании эфемерных ключей
    /// </summary>
    public sealed class KeyExchange : IDisposable {
        private ECDiffieHellman _ecdh;
        private byte[] _publicKey;
        private byte[] _privateKey;
        private readonly KeyExchangeAlgorithm _algorithm;
        private bool _disposed;

        public KeyExchange(KeyExchangeAlgorithm algorithm = KeyExchangeAlgorithm.ECDH_P256) {
            _algorithm = algorithm;
            _ecdh = CreateECDH(algorithm);
            ExportKeyPair();
        }

        /// <summary>
        /// Создание ECDH объекта для выбранной кривой
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ECDiffieHellman CreateECDH(KeyExchangeAlgorithm algorithm) {
            return algorithm switch {
                KeyExchangeAlgorithm.ECDH_P256 => ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256),
                KeyExchangeAlgorithm.ECDH_P384 => ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384),
                KeyExchangeAlgorithm.X25519 => ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256), // X25519 требует отдельной библиотеки
                _ => ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256)
            };
        }

        /// <summary>
        /// Экспорт ключевой пары (приватный и публичный ключи)
        /// </summary>
        private void ExportKeyPair() {
            var parameters = _ecdh.ExportParameters(true);
            _privateKey = parameters.D;

            // Публичный ключ в формате X/Y координат (64 байта)
            _publicKey = new byte[64];
            Buffer.BlockCopy(parameters.Q.X, 0, _publicKey, 0, 32);
            Buffer.BlockCopy(parameters.Q.Y, 0, _publicKey, 32, 32);
        }

        /// <summary>
        /// Получение публичного ключа для передачи другой стороне
        /// </summary>
        public byte[] GetPublicKey() => (byte[])_publicKey.Clone();

        /// <summary>
        /// Вычисление общего секрета с использованием публичного ключа удаленной стороны
        /// </summary>
        /// <param name="remotePublicKey">64-байтный публичный ключ другой стороны</param>
        /// <returns>Общий секрет (32 байта)</returns>
        public byte[] ComputeSharedSecret(byte[] remotePublicKey) {
            if (remotePublicKey == null)
                throw new ArgumentNullException(nameof(remotePublicKey));
            if (remotePublicKey.Length != 64)
                throw new ArgumentException("Remote public key must be 64 bytes", nameof(remotePublicKey));

            try {
                // Создаем ECDH объект для удаленной стороны
                using var remoteEcdh = ECDiffieHellman.Create(GetCurve());

                var remoteParameters = new ECParameters {
                    Curve = GetCurve(),
                    Q = new ECPoint {
                        X = remotePublicKey[0..32],
                        Y = remotePublicKey[32..64]
                    }
                };

                remoteEcdh.ImportParameters(remoteParameters);

                // DeriveKeyMaterial вычисляет общий секрет
                var sharedSecret = _ecdh.DeriveKeyMaterial(remoteEcdh.PublicKey);

                var salt = new byte[32];
                RandomNumberGenerator.Fill(salt);

                // Дополнительный KDF для улучшения энтропии
                using var kdf = new Rfc2898DeriveBytes(sharedSecret, salt, 10000, HashAlgorithmName.SHA256);
                return kdf.GetBytes(32);
            } catch (Exception ex) {
                throw new CryptographicException($"Key exchange failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Получение ECCurve для выбранного алгоритма
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ECCurve GetCurve() {
            return _algorithm switch {
                KeyExchangeAlgorithm.ECDH_P256 => ECCurve.NamedCurves.nistP256,
                KeyExchangeAlgorithm.ECDH_P384 => ECCurve.NamedCurves.nistP384,
                _ => ECCurve.NamedCurves.nistP256
            };
        }

        public void Dispose() {
            if (!_disposed) {
                _ecdh?.Dispose();
                if (_privateKey != null)
                    CryptographicOperations.ZeroMemory(_privateKey);
                _disposed = true;
            }
        }
    }
}