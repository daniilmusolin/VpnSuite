using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using VpnBot.keyboards;

namespace VpnBot.Commands;

public class StartCommand : ICommand {
    public string Name => "/start";
    public string Description => "Запуск бота и главное меню";

    private readonly IServiceProvider _services;

    public StartCommand(IServiceProvider services) {
        _services = services;
    }

    public async Task ExecuteAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken) {
        var userId = message.From?.Id ?? 0;
        var firstName = message.From?.FirstName ?? "User";

        var userManager = _services.GetRequiredService<UserManager>();
        userManager.UpdateActivity(userId);

        var welcomeMessage = $@"
            🎉 *Добро пожаловать в VPN Bot, {firstName}!* 🎉

            Этот бот позволяет управлять VPN сервером прямо из Telegram.

            *Доступные команды:*
            /stats - 📊 Статистика сервера
            /clients - 👥 Список клиентов
            /traffic - 📈 Детальная статистика трафика
            /kick <id> - 🔨 Отключить клиента
            /ban <id> - 🚫 Забанить клиента
            /help - ❓ Помощь

            *Быстрые действия:*
            Используйте кнопки ниже для быстрого доступа.
            ";

        var keyboard = MainKeyboard.GetKeyboard();

        await botClient.SendTextMessageAsync(
            message.Chat.Id,
            welcomeMessage,
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }
}