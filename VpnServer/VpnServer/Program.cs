using VpnCore.Utils;

namespace VpnServer;

class Program {
    private static ServerCore? _server;
    private static TrafficMonitor? _trafficMonitor;
    private static CancellationTokenSource? _cts;
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
║                    VPN SERVER v2.0                          ║
║              Secure VPN with Telegram API                   ║
║                                                              ║
╚══════════════════════════════════════════════════════════════╝
        ");

        _cts = new CancellationTokenSource();
        Console.CancelKeyPress += (sender, e) => {
            e.Cancel = true;
            Console.WriteLine("\n🛑 Остановка сервера...");
            _cts?.Cancel();
        };

        try {
            // Загрузка конфигурации
            var configManager = new ConfigManager<ServerConfig>("server_config.json");
            var config = await configManager.LoadAsync();

            // Создание компонентов
            _server = new ServerCore(config);
            _trafficMonitor = new TrafficMonitor();

            // Подписка на события
            _server.OnClientConnected += OnClientConnected;
            _server.OnClientDisconnected += OnClientDisconnected;
            _server.OnError += OnServerError;

            // Запуск VPN сервера
            await _server.StartAsync(_cts.Token);

            // Запуск мониторинга трафика
            _trafficMonitor.Start();

            // Запуск HTTP API для Telegram бота
            _ = Task.Run(() => StartApiServer(_server, _trafficMonitor, _cts.Token));

            Console.WriteLine("\n✅ VPN Server полностью запущен!");
            Console.WriteLine("📡 Telegram API доступен на порту 5000");
            Console.WriteLine("🔑 API Key сохранен в api_key.txt");
            Console.WriteLine("\nНажмите Ctrl+C для остановки...");

            await Task.Delay(-1, _cts.Token);
        } catch (OperationCanceledException) {
            _logger.Info("Server shutdown requested");
        } catch (Exception ex) {
            _logger.Error($"Fatal error: {ex.Message}");
            Console.WriteLine($"❌ Fatal error: {ex.Message}");
        } finally {
            await ShutdownAsync();
        }
    }

    private static async Task StartApiServer(ServerCore server, TrafficMonitor monitor, CancellationToken token) {
        try {
            var api = new VpnApiHost(server, monitor);
            await api.StartAsync(5000, token);
        } catch (Exception ex) {
            _logger.Error($"API Server error: {ex.Message}");
        }
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

    private static async Task ShutdownAsync() {
        _logger.Info("Shutting down...");
        Console.WriteLine("\n🛑 Shutting down...");

        if (_server != null) {
            await _server.StopAsync();
        }

        _trafficMonitor?.Dispose();
        _cts?.Dispose();

        _logger.Info("Server stopped");
        Console.WriteLine("✅ Server stopped. Goodbye!");
    }
}

// Простой ConfigManager для загрузки конфигурации
public class ConfigManager<T> where T : new() {
    private readonly string _path;

    public ConfigManager(string path) {
        _path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", path);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    public async Task<T> LoadAsync() {
        if (!File.Exists(_path)) {
            var defaultConfig = new T();
            await SaveAsync(defaultConfig);
            return defaultConfig;
        }

        var json = await File.ReadAllTextAsync(_path);
        return System.Text.Json.JsonSerializer.Deserialize<T>(json) ?? new T();
    }

    public async Task SaveAsync(T config) {
        var json = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_path, json);
    }
}