//using System;
//using System.Threading;
//using System.Threading.Tasks;
//using Microsoft.Extensions.Hosting;
//using Microsoft.Extensions.Logging;
//using Telegram.Bot;
//using Telegram.Bot.Polling;
//using Telegram.Bot.Types.Enums;

//namespace VpnBot {
//    public class BotHost : IHostedService {
//        private readonly BotConfig _config;
//        private readonly BotHandlers _handlers;
//        private readonly ILogger<BotHost> _logger;
//        private ITelegramBotClient _botClient;
//        private CancellationTokenSource _cts;

//        public BotHost(BotConfig config, BotHandlers handlers, ILogger<BotHost> logger) {
//            _config = config;
//            _handlers = handlers;
//            _logger = logger;
//        }

//        public async Task StartAsync(CancellationToken cancellationToken) {
//            _logger.LogInformation("Запуск Telegram бота...");

//            _botClient = new TelegramBotClient(_config.BotToken);
//            _cts = new CancellationTokenSource();

//            var botInfo = await _botClient.GetMeAsync(cancellationToken);
//            _logger.LogInformation($"Бот запущен: @{botInfo.Username}");

//            var receiverOptions = new ReceiverOptions {
//                AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery }
//            };

//            _botClient.StartReceiving(
//                _handlers.HandleUpdateAsync,
//                _handlers.HandleErrorAsync,
//                receiverOptions,
//                _cts.Token
//            );

//            _logger.LogInformation("Бот готов к работе!");
//        }

//        public async Task StopAsync(CancellationToken cancellationToken) {
//            _logger.LogInformation("Остановка бота...");
//            _cts.Cancel();
//            await Task.CompletedTask;
//        }

//        public ITelegramBotClient GetBotClient() => _botClient;
//    }
//}