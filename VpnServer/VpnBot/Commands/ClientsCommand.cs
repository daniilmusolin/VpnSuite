using Microsoft.Extensions.DependencyInjection;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace VpnBot.Commands;

public class ClientsCommand : ICommand {
    public string Name => "/clients";
    public string Description => "Список активных клиентов";

    private readonly IServiceProvider _services;

    public ClientsCommand(IServiceProvider services) {
        _services = services;
    }

    public async Task ExecuteAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken) {
        var vpnApi = _services.GetRequiredService<VpnApiClient>();
        var userManager = _services.GetRequiredService<UserManager>();
        var userId = message.From?.Id ?? 0;

        userManager.UpdateActivity(userId);

        try {
            var clients = await vpnApi.GetClientsAsync();

            if (!clients.Any()) {
                await botClient.SendTextMessageAsync(
                    message.Chat.Id,
                    "📭 *Нет активных клиентов*",
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"👥 *Активные клиенты ({clients.Count})*\n");

            // Создаем список рядов кнопок
            var buttons = new List<List<InlineKeyboardButton>>();

            foreach (var client in clients) {
                var totalMB = (client.BytesSent + client.BytesReceived) / (1024.0 * 1024);

                sb.AppendLine($"• *{client.ClientId}*");
                sb.AppendLine($"  🌐 IP: {client.VirtualIp ?? "N/A"}");
                sb.AppendLine($"  📥 ↓ {FormatBytes(client.BytesReceived)} | 📤 ↑ {FormatBytes(client.BytesSent)}");
                sb.AppendLine($"  📊 Всего: {totalMB:F1} MB");
                sb.AppendLine($"  ⏱️ Активность: {client.LastActivity:HH:mm:ss}");
                sb.AppendLine();

                // Добавляем кнопки для каждого клиента
                if (userManager.CanKick(userId)) {
                    var row = new List<InlineKeyboardButton>
                    {
                        InlineKeyboardButton.WithCallbackData($"🔨 Кик {client.ClientId}", $"kick_{client.ClientId}"),
                        InlineKeyboardButton.WithCallbackData($"🚫 Бан {client.ClientId}", $"ban_{client.ClientId}")
                    };
                    buttons.Add(row);
                }
            }

            // Добавляем кнопку обновления
            buttons.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData("🔄 Обновить", "refresh_clients")
            });

            var keyboard = new InlineKeyboardMarkup(buttons);

            await botClient.SendTextMessageAsync(
                message.Chat.Id,
                sb.ToString(),
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);
        } catch (Exception ex) {
            await botClient.SendTextMessageAsync(
                message.Chat.Id,
                $"❌ Ошибка получения списка клиентов: {ex.Message}",
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
}