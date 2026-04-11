using Telegram.Bot.Types.ReplyMarkups;

namespace VpnBot {
    public static class MainKeyboard {
        public static ReplyKeyboardMarkup GetKeyboard() {
            var keyboard = new ReplyKeyboardMarkup(new[]
            {
                new[]
                {
                    new KeyboardButton("📊 Statistics"),
                    new KeyboardButton("👥 Clients")
                },
                new[]
                {
                    new KeyboardButton("❓ Help"),
                    new KeyboardButton("🔄 Refresh")
                }
            }) {
                ResizeKeyboard = true,
                OneTimeKeyboard = false
            };

            return keyboard;
        }

        public static InlineKeyboardMarkup GetInlineKeyboard() {
            return new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("📊 Stats", "stats"),
                    InlineKeyboardButton.WithCallbackData("👥 Clients", "clients")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🔄 Refresh", "refresh"),
                    InlineKeyboardButton.WithCallbackData("❓ Help", "help")
                }
            });
        }
    }
}