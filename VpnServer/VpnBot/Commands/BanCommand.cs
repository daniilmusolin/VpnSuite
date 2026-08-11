using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace VpnBot.Commands;

public class BanCommand : ICommand {
    public string Name => "/ban";
    public string Description => "Забанить клиента (использование: /ban CLIENT_ID)";

    private readonly IServiceProvider _services;

    public BanCommand(IServiceProvider services) {
        _services = services;
    }

    public async Task ExecuteAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken) {
        var vpnApi = _services.GetRequiredService<VpnApiClient>();
        var userManager = _services.GetRequiredService<UserManager>();
        var userId = message.From?.Id ?? 0;

        userManager.UpdateActivity(userId);

        if (!userManager.CanBan(userId)) {
            await botClient.SendTextMessageAsync(
                message.Chat.Id,
                "У вас нет прав на блокировку клиентов",
                cancellationToken: cancellationToken);
            return;
        }

        var parts = message.Text?.Split(' ') ?? Array.Empty<string>();
        if (parts.Length < 2) {
            await botClient.SendTextMessageAsync(
                message.Chat.Id,
                "Использование: `/ban CLIENT_ID`\n\nИспользуйте `/clients` для просмотра ID клиентов.",
                parseMode: ParseMode.Markdown,
                cancellationToken: cancellationToken);
            return;
        }

        var clientId = parts[1];

        try {
            var result = await vpnApi.BanClientAsync(clientId);

            if (result) {
                await botClient.SendTextMessageAsync(
                    message.Chat.Id,
                    $"Клиент `{clientId}` забанен",
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);
            } else {
                await botClient.SendTextMessageAsync(
                    message.Chat.Id,
                    $"Не удалось забанить клиента `{clientId}`. Возможно, клиент не существует.",
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);
            }
        } catch (Exception ex) {
            await botClient.SendTextMessageAsync(
                message.Chat.Id,
                $"Ошибка: {ex.Message}",
                cancellationToken: cancellationToken);
        }
    }
}
