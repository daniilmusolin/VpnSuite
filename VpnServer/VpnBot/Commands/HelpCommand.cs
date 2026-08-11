using Microsoft.Extensions.DependencyInjection;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using VpnBot.keyboards;

namespace VpnBot.Commands;

public class HelpCommand : ICommand {
    public string Name => "/help";
    public string Description => "Показать справку";

    private readonly IServiceProvider _services;

    public HelpCommand(IServiceProvider services) {
        _services = services;
    }

    public async Task ExecuteAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken) {
        var userManager = _services.GetRequiredService<UserManager>();
        var userId = message.From?.Id ?? 0;
        var commands = _services.GetServices<ICommand>();

        userManager.UpdateActivity(userId);

        var sb = new StringBuilder();
        sb.AppendLine("*VPN Bot - Справка*");
        sb.AppendLine();
        sb.AppendLine("*Доступные команды:*");
        sb.AppendLine();

        foreach (var cmd in commands.OrderBy(c => c.Name)) {
            sb.AppendLine($"`{cmd.Name}` - {cmd.Description}");
        }

        sb.AppendLine();
        sb.AppendLine("*Примеры:*");
        sb.AppendLine("`/kick CLIENT_001` - отключить клиента");
        sb.AppendLine("`/ban CLIENT_002` - заблокировать клиента");
        sb.AppendLine("`/traffic CLIENT_003` - трафик клиента");
        sb.AppendLine();
        sb.AppendLine("*Быстрые действия:*");
        sb.AppendLine("Используйте кнопки меню для быстрой навигации.");

        var keyboard = MainKeyboard.GetKeyboard();

        await botClient.SendTextMessageAsync(
            message.Chat.Id,
            sb.ToString(),
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }
}
