using Telegram.Bot.Types.ReplyMarkups;

namespace VpnBot.keyboards;

public static class MainKeyboard {
    public static ReplyKeyboardMarkup GetKeyboard() {
        return new ReplyKeyboardMarkup(new[]
        {
            new[] { new KeyboardButton("📊 Statistics"), new KeyboardButton("👥 Clients") },
            new[] { new KeyboardButton("❓ Help"), new KeyboardButton("🔄 Refresh") }
        }) 
        {
            ResizeKeyboard = true,
            OneTimeKeyboard = false
        };
    }
}