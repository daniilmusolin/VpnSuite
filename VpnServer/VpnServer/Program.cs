using VpnCore.Utils;

namespace VpnServer {
    /// <summary>
    /// Точка входа VPN сервера
    /// </summary>
    class Program {
        private static ServerCore _server;
        private static CancellationTokenSource _cts;
        private static readonly Logger _logger = Logger.Instance;

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
║                    VPN SERVER v1.0                          ║
║              Secure & Fast VPN Solution                     ║
║                                                              ║
╚══════════════════════════════════════════════════════════════╝
            ");

            Console.WriteLine();
            _logger.Info("VPN Server starting...");

            // Обработка завершения работы
            Console.CancelKeyPress += OnCancelKeyPress;
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

            _cts = new CancellationTokenSource();

            try {
                // Загрузка конфигурации
                var config = await LoadConfigAsync();

                // Создание и запуск сервера
                _server = new ServerCore(config);
                _server.OnClientConnected += OnClientConnected;
                _server.OnClientDisconnected += OnClientDisconnected;
                _server.OnError += OnServerError;

                await _server.StartAsync(_cts.Token);

                // Ожидание завершения
                Console.WriteLine("\nPress Ctrl+C to stop the server...");
                await Task.Delay(-1, _cts.Token);
            } catch (OperationCanceledException) {
                _logger.Info("Server shutdown requested");
            } catch (Exception ex) {
                _logger.Error($"Server fatal error: {ex.Message}");
                Console.WriteLine($"Fatal error: {ex.Message}");
            } finally {
                await ShutdownAsync();
            }
        }

        private static async Task<ServerConfig> LoadConfigAsync() {
            var configManager = new ConfigManager<ServerConfig>("server_config.json");
            var config = await configManager.LoadAsync();

            Console.WriteLine($"Configuration loaded:");
            Console.WriteLine($"  Listen Address: {config.ListenAddress}:{config.ListenPort}");
            Console.WriteLine($"  Max Clients: {config.MaxClients}");
            Console.WriteLine($"  Virtual Network: {config.VirtualNetwork}");
            Console.WriteLine($"  MTU: {config.Mtu}");
            Console.WriteLine();

            return config;
        }

        private static void OnClientConnected(string clientId, string remoteAddress) {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[+] Client connected: {clientId} from {remoteAddress}");
            Console.ResetColor();
            _logger.Info($"Client connected: {clientId} from {remoteAddress}");
        }

        private static void OnClientDisconnected(string clientId, string reason) {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[-] Client disconnected: {clientId} - {reason}");
            Console.ResetColor();
            _logger.Info($"Client disconnected: {clientId} - {reason}");
        }

        private static void OnServerError(string error) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[!] Server error: {error}");
            Console.ResetColor();
            _logger.Error($"Server error: {error}");
        }

        private static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e) {
            e.Cancel = true;
            _logger.Info("Ctrl+C pressed, shutting down...");
            _cts.Cancel();
        }

        private static void OnProcessExit(object sender, EventArgs e) {
            _logger.Info("Process exiting...");
            _cts?.Cancel();
        }

        private static async Task ShutdownAsync() {
            _logger.Info("Shutting down server...");
            Console.WriteLine("\nShutting down...");

            if (_server != null) {
                await _server.StopAsync();
            }

            _cts?.Dispose();
            _logger.Info("Server stopped");
            Console.WriteLine("Server stopped. Goodbye!");
        }
    }
}