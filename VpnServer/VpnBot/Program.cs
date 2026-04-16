using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VpnBot.Commands;

namespace VpnBot;

class Program {
    static async Task Main(string[] args) {
        Console.WriteLine(@"
╔══════════════════════════════════════════════════════════════╗
║                                                              ║
║     ██╗   ██╗██████╗ ███╗   ██╗                             ║
║     ██║   ██║██╔══██╗████╗  ██║                             ║
║     ██║   ██║██████╔╝██╔██╗ ██║                             ║
║     ╚██╗ ██╔╝██╔═══╝ ██║╚██╗██║                             ║
║      ╚████╔╝ ██║     ██║ ╚████║                             ║
║       ╚═══╝  ╚═╝     ╚═╝  ╚═══╝                             ║
║                                                              ║
║              ТЕЛЕГРАМ БОТ ДЛЯ VPN v2.0                       ║
║            Управление VPN сервером из Telegram              ║
║                                                              ║
╚══════════════════════════════════════════════════════════════╝
        ");

        var host = CreateHostBuilder(args).Build();

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (sender, e) => {
            e.Cancel = true;
            Console.WriteLine("\n🛑 Остановка бота...");
            cts.Cancel();
        };

        try {
            await host.StartAsync(cts.Token);
            Console.WriteLine("✅ Бот запущен! Нажмите Ctrl+C для остановки.");
            await Task.Delay(-1, cts.Token);
        } catch (OperationCanceledException) {
            Console.WriteLine("Бот остановлен.");
        } finally {
            await host.StopAsync();
            host.Dispose();
        }
    }

    static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) => {
                // Загружаем конфигурацию
                var config = BotConfig.Load();
                services.AddSingleton(config);

                // Сервисы бота
                services.AddSingleton<VpnApiClient>();
                services.AddSingleton<UserManager>();
                services.AddSingleton<BotHandlers>();
                services.AddSingleton<BotHost>();

                // Команды
                services.AddSingleton<ICommand, StartCommand>();
                services.AddSingleton<ICommand, StatsCommand>();
                services.AddSingleton<ICommand, ClientsCommand>();
                services.AddSingleton<ICommand, KickCommand>();
                services.AddSingleton<ICommand, BanCommand>();
                services.AddSingleton<ICommand, TrafficCommand>();
                services.AddSingleton<ICommand, HelpCommand>();

                services.AddHttpClient();
                services.AddLogging(builder => {
                    builder.AddConsole();
                    builder.AddFilter(level => level >= LogLevel.Information);
                });
            });
}