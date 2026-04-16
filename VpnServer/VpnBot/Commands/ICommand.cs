using Telegram.Bot;
using Telegram.Bot.Types;

namespace VpnBot.Commands;

public interface ICommand {
    string Name { get; }
    string Description { get; }
    Task ExecuteAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken);
}