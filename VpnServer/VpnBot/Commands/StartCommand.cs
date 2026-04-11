//using System;
//using System.Threading;
//using System.Threading.Tasks;
//using Microsoft.Extensions.DependencyInjection;
//using Telegram.Bot;
//using Telegram.Bot.Types;
//using VpnBot.keyboards;

//namespace VpnBot.Commands {
//    public class StartCommand : ICommand {
//        public string Name => "/start";
//        public string Description => "Start the bot and show main menu";

//        private readonly IServiceProvider _services;

//        public StartCommand(IServiceProvider services) {
//            _services = services;
//        }

//        public async Task ExecuteAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken) {
//            var userId = message.From.Id;
//            var username = message.From.Username;
//            var firstName = message.From.FirstName;

//            var userManager = _services.GetRequiredService<UserManager>();
//            userManager.UpdateActivity(userId);

//            var welcomeMessage = $@"
//                🎉 *Welcome to VPN Bot, {firstName}!* 🎉

//                This bot allows you to manage your VPN server directly from Telegram.

//                *Available commands:*
//                /stats - 📊 View server statistics
//                /clients - 👥 View and manage connected clients
//                /kick <client_id> - 🔨 Kick a client
//                /ban <client_id> - 🚫 Ban a client
//                /help - ❓ Show this help message

//                *Quick actions:*
//                Use the buttons below for quick access to main features.

//                ⚡ *Pro tip:* You can also use inline buttons for faster navigation!
//                            ";

//            var keyboard = MainKeyboard.GetKeyboard();

//            await botClient.SendTextMessageAsync(
//                message.Chat.Id,
//                welcomeMessage,
//                ParseMode.Markdown,
//                replyMarkup: keyboard,
//                cancellationToken: cancellationToken);
//        }
//    }
//}