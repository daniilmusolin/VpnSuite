//using System.Threading;
//using System.Threading.Tasks;
//using Microsoft.Extensions.DependencyInjection;
//using Telegram.Bot;
//using Telegram.Bot.Types;

//namespace VpnBot.Commands {
//    public class BanCommand : ICommand {
//        public string Name => "/ban";
//        public string Description => "Ban a client (usage: /ban CLIENT_ID)";

//        private readonly IServiceProvider _services;

//        public BanCommand(IServiceProvider services) {
//            _services = services;
//        }

//        public async Task ExecuteAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken) {
//            var vpnApi = _services.GetRequiredService<VpnApiClient>();
//            var userManager = _services.GetRequiredService<UserManager>();

//            userManager.UpdateActivity(message.From.Id);

//            var parts = message.Text.Split(' ');
//            if (parts.Length < 2) {
//                await botClient.SendTextMessageAsync(
//                    message.Chat.Id,
//                    "❌ Usage: `/ban CLIENT_ID`\n\nUse `/clients` to see available client IDs.",
//                    ParseMode.Markdown,
//                    cancellationToken: cancellationToken);
//                return;
//            }

//            var clientId = parts[1];

//            try {
//                var result = await vpnApi.BanClientAsync(clientId);

//                if (result) {
//                    await botClient.SendTextMessageAsync(
//                        message.Chat.Id,
//                        $"✅ Client `{clientId}` has been banned successfully.",
//                        ParseMode.Markdown,
//                        cancellationToken: cancellationToken);
//                } else {
//                    await botClient.SendTextMessageAsync(
//                        message.Chat.Id,
//                        $"❌ Failed to ban client `{clientId}`. Client may not exist.",
//                        ParseMode.Markdown,
//                        cancellationToken: cancellationToken);
//                }
//            } catch (Exception ex) {
//                await botClient.SendTextMessageAsync(
//                    message.Chat.Id,
//                    $"❌ Error: {ex.Message}",
//                    cancellationToken: cancellationToken);
//            }
//        }
//    }
//}