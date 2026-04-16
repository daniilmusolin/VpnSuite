using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using VpnBot.Commands;
using VpnBot.keyboards;

namespace VpnBot;

public class BotHandlers {
    private readonly UserManager _userManager;
    private readonly VpnApiClient _vpnApi;
    private readonly ILogger<BotHandlers> _logger;
    private readonly Dictionary<string, ICommand> _commands;

    public BotHandlers(
        IServiceProvider services,
        UserManager userManager,
        VpnApiClient vpnApi,
        ILogger<BotHandlers> logger,
        IEnumerable<ICommand> commands) {
        _userManager = userManager;
        _vpnApi = vpnApi;
        _logger = logger;
        _commands = commands.ToDictionary(c => c.Name.ToLower(), c => c);
    }

    public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken) {
        try {
            if (update.Message is { } message) {
                await HandleMessage(botClient, message, cancellationToken);
            } else if (update.CallbackQuery is { } callbackQuery) {
                await HandleCallbackQuery(botClient, callbackQuery, cancellationToken);
            }
        } catch (Exception ex) {
            _logger.LogError(ex, "Error handling update");
        }
    }

    private async Task HandleMessage(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken) {
        var userId = message.From?.Id ?? 0;
        var chatId = message.Chat.Id;

        if (!_userManager.IsAuthorized(userId)) {
            await botClient.SendTextMessageAsync(chatId, "⛔ У вас нет доступа к этому боту.", cancellationToken: cancellationToken);
            return;
        }

        _userManager.UpdateActivity(userId);

        if (message.Text is not { } messageText)
            return;

        if (messageText.StartsWith("/")) {
            var commandName = messageText.Split(' ')[0].ToLower();
            if (_commands.TryGetValue(commandName, out var command)) {
                await command.ExecuteAsync(botClient, message, cancellationToken);
            } else {
                await botClient.SendTextMessageAsync(chatId, "❓ Неизвестная команда. Используйте /help", cancellationToken: cancellationToken);
            }
        } else {
            switch (messageText) {
                case "📊 Statistics":
                    await _commands["/stats"].ExecuteAsync(botClient, message, cancellationToken);
                    break;
                case "👥 Clients":
                    await _commands["/clients"].ExecuteAsync(botClient, message, cancellationToken);
                    break;
                case "🔄 Refresh":
                    await _commands["/stats"].ExecuteAsync(botClient, message, cancellationToken);
                    break;
                case "❓ Help":
                    await _commands["/help"].ExecuteAsync(botClient, message, cancellationToken);
                    break;
                default:
                    await botClient.SendTextMessageAsync(chatId, "Используйте кнопки меню для навигации.", replyMarkup: MainKeyboard.GetKeyboard(), cancellationToken: cancellationToken);
                    break;
            }
        }
    }

    private async Task HandleCallbackQuery(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken cancellationToken) {
        var userId = callbackQuery.From.Id;
        var chatId = callbackQuery.Message?.Chat.Id ?? 0;

        if (!_userManager.IsAuthorized(userId)) {
            await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "⛔ Нет доступа", cancellationToken: cancellationToken);
            return;
        }

        var data = callbackQuery.Data;
        if (data == null) return;

        if (data == "refresh_stats") {
            var fakeMessage = new Message { Chat = new Chat { Id = chatId }, From = callbackQuery.From, Text = "/stats" };
            await _commands["/stats"].ExecuteAsync(botClient, fakeMessage, cancellationToken);
        } else if (data == "refresh_clients") {
            var fakeMessage = new Message { Chat = new Chat { Id = chatId }, From = callbackQuery.From, Text = "/clients" };
            await _commands["/clients"].ExecuteAsync(botClient, fakeMessage, cancellationToken);
        } else if (data.StartsWith("kick_")) {
            var clientId = data.Replace("kick_", "");
            if (_userManager.CanKick(userId)) {
                await _vpnApi.KickClientAsync(clientId);
                await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, $"✅ Клиент {clientId} отключен", cancellationToken: cancellationToken);

                var fakeMessage = new Message { Chat = new Chat { Id = chatId }, From = callbackQuery.From, Text = "/clients" };
                await _commands["/clients"].ExecuteAsync(botClient, fakeMessage, cancellationToken);
            } else {
                await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "⛔ У вас нет прав на кик", cancellationToken: cancellationToken);
            }
        } else if (data.StartsWith("ban_")) {
            var clientId = data.Replace("ban_", "");
            if (_userManager.CanBan(userId)) {
                await _vpnApi.BanClientAsync(clientId);
                await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, $"🚫 Клиент {clientId} забанен", cancellationToken: cancellationToken);

                var fakeMessage = new Message { Chat = new Chat { Id = chatId }, From = callbackQuery.From, Text = "/clients" };
                await _commands["/clients"].ExecuteAsync(botClient, fakeMessage, cancellationToken);
            } else {
                await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "⛔ У вас нет прав на бан", cancellationToken: cancellationToken);
            }
        }

        await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken: cancellationToken);
    }

    public Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken) {
        _logger.LogError(exception, "Telegram Bot Error");
        return Task.CompletedTask;
    }
}