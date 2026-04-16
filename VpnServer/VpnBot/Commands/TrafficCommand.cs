using Microsoft.Extensions.DependencyInjection;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace VpnBot.Commands;

public class TrafficCommand : ICommand {
    public string Name => "/traffic";
    public string Description => "Детальная статистика трафика";

    private readonly IServiceProvider _services;

    public TrafficCommand(IServiceProvider services) {
        _services = services;
    }

    public async Task ExecuteAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken) {
        var vpnApi = _services.GetRequiredService<VpnApiClient>();
        var userManager = _services.GetRequiredService<UserManager>();

        userManager.UpdateActivity(message.From?.Id ?? 0);

        var parts = message.Text?.Split(' ') ?? Array.Empty<string>();
        var clientId = parts.Length > 1 ? parts[1] : null;

        try {
            if (string.IsNullOrEmpty(clientId)) {
                // Общая статистика
                var stats = await vpnApi.GetServerStatsAsync();

                var totalMB = (stats.TotalBytesSent + stats.TotalBytesReceived) / (1024.0 * 1024);
                var avgSpeed = (stats.CurrentSendSpeed + stats.CurrentReceiveSpeed) / 2;

                var report = new StringBuilder();
                report.AppendLine("📊 *ОБЩАЯ СТАТИСТИКА ТРАФИКА*");
                report.AppendLine("━━━━━━━━━━━━━━━━━━━━━━");
                report.AppendLine($"📥 *Загружено:* {FormatBytes(stats.TotalBytesReceived)}");
                report.AppendLine($"📤 *Отправлено:* {FormatBytes(stats.TotalBytesSent)}");
                report.AppendLine($"📦 *Всего:* {totalMB:F1} MB");
                report.AppendLine("━━━━━━━━━━━━━━━━━━━━━━");
                report.AppendLine($"⚡ *Скорость загрузки:* {FormatSpeed(stats.CurrentReceiveSpeed)}");
                report.AppendLine($"⚡ *Скорость отправки:* {FormatSpeed(stats.CurrentSendSpeed)}");
                report.AppendLine($"📈 *Средняя скорость:* {FormatSpeed((long)avgSpeed)}");
                report.AppendLine("━━━━━━━━━━━━━━━━━━━━━━");
                report.AppendLine($"👥 *Активных клиентов:* {stats.ActiveClients}");

                await botClient.SendTextMessageAsync(
                    message.Chat.Id,
                    report.ToString(),
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);
            } else {
                // Статистика конкретного клиента
                var clients = await vpnApi.GetClientsAsync();
                var client = clients.FirstOrDefault(c => c.ClientId == clientId);

                if (client == null) {
                    await botClient.SendTextMessageAsync(
                        message.Chat.Id,
                        $"❌ Клиент `{clientId}` не найден",
                        parseMode: ParseMode.Markdown,
                        cancellationToken: cancellationToken);
                    return;
                }

                var sentMB = client.BytesSent / (1024.0 * 1024);
                var receivedMB = client.BytesReceived / (1024.0 * 1024);

                var report = new StringBuilder();
                report.AppendLine($"📊 *СТАТИСТИКА КЛИЕНТА*");
                report.AppendLine($"━━━━━━━━━━━━━━━━━━━━━━");
                report.AppendLine($"🆔 *ID:* `{client.ClientId}`");
                report.AppendLine($"🌐 *VPN IP:* {client.VirtualIp ?? "N/A"}");
                report.AppendLine($"📍 *Реальный IP:* {client.RemoteEndpoint ?? "N/A"}");
                report.AppendLine("━━━━━━━━━━━━━━━━━━━━━━");
                report.AppendLine($"📤 *Отправлено:* {sentMB:F1} MB");
                report.AppendLine($"📥 *Получено:* {receivedMB:F1} MB");
                report.AppendLine($"📦 *Всего:* {sentMB + receivedMB:F1} MB");
                report.AppendLine("━━━━━━━━━━━━━━━━━━━━━━");
                report.AppendLine($"📦 *Пакетов:* {client.PacketsSent:N0} / {client.PacketsReceived:N0}");
                report.AppendLine($"⏱️ *Активность:* {client.LastActivity:HH:mm:ss}");
                report.AppendLine($"🔐 *Аутентифицирован:* {(client.IsAuthenticated ? "✅ Да" : "❌ Нет")}");

                await botClient.SendTextMessageAsync(
                    message.Chat.Id,
                    report.ToString(),
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);
            }
        } catch (Exception ex) {
            await botClient.SendTextMessageAsync(
                message.Chat.Id,
                $"❌ Ошибка: {ex.Message}",
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