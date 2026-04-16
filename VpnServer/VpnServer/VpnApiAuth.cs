using System.Security.Cryptography;

namespace VpnServer;

public static class VpnApiAuth {
    private static string? _apiKey;
    private static readonly object _lock = new();

    public static string GetOrGenerateApiKey() {
        if (_apiKey != null) return _apiKey;

        lock (_lock) {
            var keyFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "api_key.txt");

            if (File.Exists(keyFile)) {
                _apiKey = File.ReadAllText(keyFile).Trim();
            } else {
                // Генерируем безопасный 48-символьный ключ
                var bytes = RandomNumberGenerator.GetBytes(36);
                _apiKey = Convert.ToBase64String(bytes)
                    .Replace("/", "")
                    .Replace("+", "")
                    .Replace("=", "")
                    .Substring(0, 48);

                File.WriteAllText(keyFile, _apiKey);
                Console.WriteLine($"🔑 Сгенерирован новый API ключ: {_apiKey}");
                Console.WriteLine("⚠️ СОХРАНИТЕ ЕГО! Он нужен для Telegram бота");
            }
        }

        return _apiKey;
    }
}