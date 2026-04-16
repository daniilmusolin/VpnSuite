using System.Text.Json;

namespace VpnBot;

public enum UserRole {
    Owner,      // Полный доступ
    Admin,      // Статистика + кик
    Viewer,     // Только просмотр
    Banned      // Заблокирован
}

public class BotConfig {
    public string BotToken { get; set; } = "";
    public string VpnApiHost { get; set; } = "localhost";
    public int VpnApiPort { get; set; } = 5000;
    public long OwnerId { get; set; }
    public List<long> AdminIds { get; set; } = new();
    public List<long> ViewerIds { get; set; } = new();

    public static BotConfig Load() {
        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bot_config.json");

        if (!File.Exists(configPath)) {
            var defaultConfig = new BotConfig {
                BotToken = "YOUR_BOT_TOKEN_HERE",
                VpnApiHost = "localhost",
                VpnApiPort = 5000,
                OwnerId = 0,
                AdminIds = new List<long>(),
                ViewerIds = new List<long>()
            };

            var json = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configPath, json);

            throw new InvalidOperationException($"Пожалуйста, отредактируйте {configPath} и укажите BotToken и OwnerId");
        }

        var configJson = File.ReadAllText(configPath);
        return JsonSerializer.Deserialize<BotConfig>(configJson) ?? throw new InvalidOperationException("Failed to load config");
    }
}