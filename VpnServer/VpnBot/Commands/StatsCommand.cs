using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace VpnBot.Commands;

public class StatsCommand : ICommand {
    public string Name => "/stats";
    public string Description => "Статистика VPN сервера";

    private readonly IServiceProvider _services;

    public StatsCommand(IServiceProvider services) {
        _services = services;
    }

    public async Task ExecuteAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken) {
        var vpnApi = _services.GetRequiredService<VpnApiClient>();
        var userManager = _services.GetRequiredService<UserManager>();

        userManager.UpdateActivity(message.From?.Id ?? 0);

        try {
            var stats = await vpnApi.GetServerStatsAsync();

            var statusIcon = stats.IsRunning ? "🟢" : "🔴";
            var statusText = stats.IsRunning ? "РАБОТАЕТ" : "ОСТАНОВЛЕН";

            var statsMessage = $@"
                {statusIcon} *СТАТИСТИКА VPN СЕРВЕРА*

                ━━━━━━━━━━━━━━━━━━━━━━
                📡 *Статус:* {statusText}
                👥 *Активных клиентов:* {stats.ActiveClients}
                ━━━━━━━━━━━━━━━━━━━━━━

                📥 *Загрузка:* {FormatBytes(stats.TotalBytesReceived)}
                📤 *Отправка:* {FormatBytes(stats.TotalBytesSent)}
                ━━━━━━━━━━━━━━━━━━━━━━

                ⚡ *Скорость загрузки:* {FormatSpeed(stats.CurrentReceiveSpeed)}
                ⚡ *Скорость отправки:* {FormatSpeed(stats.CurrentSendSpeed)}
                ━━━━━━━━━━━━━━━━━━━━━━

                🔐 *Шифрование:* {stats.CipherSuite ?? "AES-256-GCM"}
                📅 *Время работы:* {stats.Uptime ?? "N/A"}

                🕐 *Обновлено:* {DateTime.Now:HH:mm:ss}
                ";

            var inlineKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("🔄 Обновить", "refresh_stats") }
            });

            await botClient.SendTextMessageAsync(
                message.Chat.Id,
                statsMessage,
                parseMode: ParseMode.Markdown,
                replyMarkup: inlineKeyboard,
                cancellationToken: cancellationToken);
        } catch (Exception ex) {
            await botClient.SendTextMessageAsync(
                message.Chat.Id,
                $"❌ Ошибка получения статистики: {ex.Message}",
                cancellationToken: cancellationToken);
        }
    }

    private string FormatBytes(long bytes) {
        if (bytes >= 1024L * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        if (bytes >= 1024 * 1024)
            return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes >= 1024)
            return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }

    private string FormatSpeed(long bytesPerSecond) {
        if (bytesPerSecond >= 1024 * 1024)
            return $"{bytesPerSecond / (1024.0 * 1024):F1} MB/s";
        if (bytesPerSecond >= 1024)
            return $"{bytesPerSecond / 1024.0:F1} KB/s";
        return $"{bytesPerSecond} B/s";
    }
}