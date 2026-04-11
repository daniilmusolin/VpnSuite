//using Microsoft.Extensions.DependencyInjection;
//using Telegram.Bot;
//using Telegram.Bot.Types;

//namespace VpnBot.Commands {
//    public class HelpCommand : ICommand {
//        public string Name => "/help";
//        public string Description => "Show all available commands";

//        private readonly IServiceProvider _services;

//        public HelpCommand(IServiceProvider services) {
//            _services = services;
//        }

//        public async Task ExecuteAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken) {
//            var userManager = _services.GetRequiredService<UserManager>();
//            userManager.UpdateActivity(message.From.Id);

//            var helpMessage = @"
//                🤖 *VPN Bot Help*

//                *Available Commands:*

//                /stats - 📊 View server statistics
//                /clients - 👥 View connected clients
//                /kick <client_id> - 🔨 Kick a client by ID
//                /ban <client_id> - 🚫 Ban a client by ID
//                /start - 🎉 Show welcome message
//                /help - ❓ Show this help

//                *Examples:*
//                `/kick CLIENT_001`
//                `/ban CLIENT_002`

//                *Quick Actions:*
//                Use the buttons in the main menu for faster access.

//                📌 *Note:* Some commands may be restricted to authorized users only.
//                            ";

//            var keyboard = MainKeyboard.GetKeyboard();

//            await botClient.SendTextMessageAsync(
//                message.Chat.Id,
//                helpMessage,
//                ParseMode.Markdown,
//                replyMarkup: keyboard,
//                cancellationToken: cancellationToken);
//        }
//    }
//}