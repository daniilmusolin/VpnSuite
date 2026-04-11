//using System;
//using System.Linq;
//using System.Text;
//using System.Threading;
//using System.Threading.Tasks;
//using Microsoft.Extensions.DependencyInjection;
//using Telegram.Bot;
//using Telegram.Bot.Types;

//namespace VpnBot.Commands {
//    public class ClientsCommand : ICommand {
//        public string Name => "/clients";
//        public string Description => "Show active VPN clients";

//        private readonly IServiceProvider _services;

//        public ClientsCommand(IServiceProvider services) {
//            _services = services;
//        }

//        public async Task ExecuteAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken) {
//            var vpnApi = _services.GetRequiredService<VpnApiClient>();
//            var userManager = _services.GetRequiredService<UserManager>();

//            userManager.UpdateActivity(message.From.Id);

//            try {
//                var clients = await vpnApi.GetClientsAsync();

//                if (!clients.Any()) {
//                    await botClient.SendTextMessageAsync(
//                        message.Chat.Id,
//                        "📭 *No active clients*",
//                        ParseMode.Markdown,
//                        cancellationToken: cancellationToken);
//                    return;
//                }

//                var sb = new StringBuilder();
//                sb.AppendLine($"👥 *Active Clients ({clients.Count})*\n");

//                var buttons = new List<List<InlineKeyboardButton>>();

//                foreach (var client in clients) {
//                    sb.AppendLine($"• *{client.ClientId}*");
//                    sb.AppendLine($"  🌐 IP: {client.VirtualIp ?? "N/A"}");
//                    sb.AppendLine($"  📥 ↓ {FormatBytes(client.BytesReceived)} | 📤 ↑ {FormatBytes(client.BytesSent)}");
//                    sb.AppendLine($"  📦 Packets: {client.PacketsReceived}/{client.PacketsSent}");
//                    sb.AppendLine($"  ⏱️ Last active: {client.LastActivity:HH:mm:ss}");
//                    sb.AppendLine();

//                    buttons.Add(new[]
//                    {
//                        InlineKeyboardButton.WithCallbackData($"🔨 Kick {client.ClientId}", $"kick_{client.ClientId}"),
//                        InlineKeyboardButton.WithCallbackData($"🚫 Ban {client.ClientId}", $"ban_{client.ClientId}")
//                    });
//                }

//                buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("🔄 Refresh", "refresh_clients") });

//                var keyboard = new InlineKeyboardMarkup(buttons);

//                await botClient.SendTextMessageAsync(
//                    message.Chat.Id,
//                    sb.ToString(),
//                    ParseMode.Markdown,
//                    replyMarkup: keyboard,
//                    cancellationToken: cancellationToken);
//            } catch (Exception ex) {
//                await botClient.SendTextMessageAsync(
//                    message.Chat.Id,
//                    $"❌ Failed to get clients: {ex.Message}",
//                    cancellationToken: cancellationToken);
//            }
//        }

//        private string FormatBytes(long bytes) {
//            if (bytes >= 1024 * 1024 * 1024)
//                return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
//            if (bytes >= 1024 * 1024)
//                return $"{bytes / (1024.0 * 1024):F1} MB";
//            if (bytes >= 1024)
//                return $"{bytes / 1024.0:F1} KB";
//            return $"{bytes} B";
//        }
//    }
//}