using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpnCore.Utils {
    /// <summary>
    /// Менеджер конфигурации
    /// Загружает и сохраняет настройки в JSON файл
    /// Поддерживает шифрование чувствительных данных
    /// </summary>
    /// <typeparam name="T">Тип конфигурации</typeparam>
    public sealed class ConfigManager<T> where T : class, new() {
        private readonly string _configPath;
        private readonly string _configDirectory;
        private T _config;
        private readonly object _lock = new object();
        private readonly bool _encryptSensitiveData;

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true
        };

        public ConfigManager(string configName = "config.json", bool encryptSensitiveData = false) {
            _configDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config");
            if (!Directory.Exists(_configDirectory))
                Directory.CreateDirectory(_configDirectory);

            _configPath = Path.Combine(_configDirectory, configName);
            _encryptSensitiveData = encryptSensitiveData;
        }

        /// <summary>
        /// Загрузка конфигурации из файла
        /// </summary>
        public async Task<T> LoadAsync() {
            lock (_lock) {
                if (_config != null)
                    return _config;
            }

            if (!File.Exists(_configPath)) {
                _config = new T();
                await SaveAsync();
                return _config;
            }

            var json = await File.ReadAllTextAsync(_configPath);

            // Дешифруем если нужно
            if (_encryptSensitiveData && json.StartsWith("ENC:")) {
                json = DecryptJson(json.Substring(4));
            }

            _config = JsonSerializer.Deserialize<T>(json, _jsonOptions) ?? new T();
            return _config;
        }

        /// <summary>
        /// Сохранение конфигурации в файл
        /// </summary>
        public async Task SaveAsync() {
            lock (_lock) {
                if (_config == null)
                    throw new InvalidOperationException("Config not loaded");
            }

            var json = JsonSerializer.Serialize(_config, _jsonOptions);

            // Шифруем если нужно
            if (_encryptSensitiveData) {
                json = "ENC:" + EncryptJson(json);
            }

            await File.WriteAllTextAsync(_configPath, json);
        }

        /// <summary>
        /// Получение текущей конфигурации
        /// </summary>
        public T Get() {
            lock (_lock) {
                return _config ?? throw new InvalidOperationException("Config not loaded");
            }
        }

        /// <summary>
        /// Обновление конфигурации
        /// </summary>
        public async Task UpdateAsync(Action<T> updateAction) {
            lock (_lock) {
                updateAction(_config);
            }
            await SaveAsync();
        }

        /// <summary>
        /// Сброс к значениям по умолчанию
        /// </summary>
        public async Task ResetToDefaultAsync() {
            _config = new T();
            await SaveAsync();
        }

        private string EncryptJson(string json) {
            // Упрощенное шифрование для демонстрации
            // В реальном проекте используйте защищенное шифрование
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            return Convert.ToBase64String(bytes);
        }

        private string DecryptJson(string encrypted) {
            var bytes = Convert.FromBase64String(encrypted);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
    }

    /// <summary>
    /// Пример конфигурации VPN
    /// </summary>
    public class VpnConfig {
        public string ServerAddress { get; set; } = "127.0.0.1";
        public int ServerPort { get; set; } = 51820;
        public string PrivateKey { get; set; } = "";
        public string PublicKey { get; set; } = "";
        public bool EnableEncryption { get; set; } = true;
        public bool EnableCompression { get; set; } = false;
        public int Mtu { get; set; } = 1400;
        public int KeepAliveInterval { get; set; } = 25;
        public int HandshakeTimeout { get; set; } = 5000;
        public int ReconnectDelay { get; set; } = 3000;
        public int MaxRetries { get; set; } = 5;
        public bool EnableLogging { get; set; } = true;
        public LogLevel MinLogLevel { get; set; } = LogLevel.Info;

        // Продвинутые настройки
        public bool EnableNatTraversal { get; set; } = true;
        public bool EnableMultiplexing { get; set; } = true;
        public int MaxStreams { get; set; } = 100;
        public string CipherSuite { get; set; } = "AES-256-GCM";

        public void Validate() {
            if (string.IsNullOrEmpty(ServerAddress))
                throw new InvalidOperationException("Server address is required");

            if (ServerPort < 1 || ServerPort > 65535)
                throw new InvalidOperationException("Invalid server port");

            if (Mtu < 576 || Mtu > 9000)
                throw new InvalidOperationException("MTU must be between 576 and 9000");
        }
    }
}