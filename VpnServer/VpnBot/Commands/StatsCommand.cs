//using System;
//using System.Threading;
//using System.Threading.Tasks;
//using Microsoft.Extensions.DependencyInjection;
//using Telegram.Bot;
//using Telegram.Bot.Types;

//namespace VpnBot.Commands {
//    public class StatsCommand : ICommand {
//        public string Name => "/stats";
//        public string Description => "Show VPN server statistics";

//        private readonly IServiceProvider _services;

//        public StatsCommand(IServiceProvider services) {
//            _services = services;
//        }

//        public async Task ExecuteAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken) {
//            var vpnApi = _services.GetRequiredService<VpnApiClient>();
//            var userManager = _services.GetRequiredService<UserManager>();

//            userManager.UpdateActivity(message.From.Id);

//            try {
//                var stats = await vpnApi.GetServerStatsAsync();

//                var statusIcon = stats.IsRunning ? "🟢" : "🔴";
//                var statusText = stats.IsRunning ? "Running" : "Stopped";

//                var statsMessage = $@"
//                    {statusIcon} *VPN Server Statistics*

//                    *Status:* {statusText}
//                    *Active Clients:* {stats.ActiveClients}
//                    *Total Download:* {FormatBytes(stats.TotalBytesReceived)}
//                    *Total Upload:* {FormatBytes(stats.TotalBytesSent)}
//                    *Current Download Speed:* {FormatSpeed(stats.CurrentReceiveSpeed)}
//                    *Current Upload Speed:* {FormatSpeed(stats.CurrentSendSpeed)}
//                    *Uptime:* {stats.Uptime ?? "N/A"}
//                    *Encryption:* {stats.CipherSuite ?? "AES-256-GCM"}

//                    📅 *Last updated:* {DateTime.Now:HH:mm:ss}
//                ";

//                var inlineKeyboard = new InlineKeyboardMarkup(new[]
//                {
//                    new[] { InlineKeyboardButton.WithCallbackData("🔄 Refresh", "refresh_stats") }
//                });

//                await botClient.SendTextMessageAsync(
//                    message.Chat.Id,
//                    statsMessage,
//                    ParseMode.Markdown,
//                    replyMarkup: inlineKeyboard,
//                    cancellationToken: cancellationToken);
//            } catch (Exception ex) {
//                await botClient.SendTextMessageAsync(
//                    message.Chat.Id,
//                    $"❌ Failed to get statistics: {ex.Message}",
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

//        private string FormatSpeed(long bytesPerSecond) {
//            if (bytesPerSecond >= 1024 * 1024)
//                return $"{bytesPerSecond / (1024.0 * 1024):F1} MB/s";
//            if (bytesPerSecond >= 1024)
//                return $"{bytesPerSecond / 1024.0:F1} KB/s";
//            return $"{bytesPerSecond} B/s";
//        }
//    }
//}