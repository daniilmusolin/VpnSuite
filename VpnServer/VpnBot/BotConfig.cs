using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace VpnBot {
    /// <summary>
    /// Роли пользователей бота
    /// </summary>
    public enum UserRole {
        Owner,      // Создатель - полный доступ (kick, ban, add_admin, remove_admin)
        Admin,      // Администратор - может смотреть статистику и кикать
        Viewer,     // Наблюдатель - только просмотр статистики
        Banned      // Заблокированный - нет доступа
    }

    public class BotConfig {
        public string BotToken { get; set; }
        public string VpnApiHost { get; set; } = "localhost";
        public int VpnApiPort { get; set; } = 5000;

        // ID создателя (владелец бота)
        public long OwnerId { get; set; }

        // Список администраторов (могут кикать)
        public List<long> AdminIds { get; set; } = new List<long>();

        // Список наблюдателей (только просмотр)
        public List<long> ViewerIds { get; set; } = new List<long>();

        public static BotConfig Load() {
            var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bot_config.json");

            if (!File.Exists(configPath)) {
                var defaultConfig = new BotConfig {
                    BotToken = "YOUR_BOT_TOKEN_HERE",
                    VpnApiHost = "localhost",
                    VpnApiPort = 5000,
                    OwnerId = 123456789, // Замените на ваш Telegram ID
                    AdminIds = new List<long>(),
                    ViewerIds = new List<long>()
                };

                var json = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configPath, json);

                throw new InvalidOperationException($"Please edit {configPath} and set your BotToken and OwnerId");
            }

            var configJson = File.ReadAllText(configPath);
            return JsonSerializer.Deserialize<BotConfig>(configJson);
        }
    }
}