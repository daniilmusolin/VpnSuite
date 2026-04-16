using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace VpnServer;

public class VpnApiHost {
    private readonly ServerCore _server;
    private readonly TrafficMonitor _trafficMonitor;
    private WebApplication? _app;
    private string? _apiKey;

    public VpnApiHost(ServerCore server, TrafficMonitor trafficMonitor) {
        _server = server;
        _trafficMonitor = trafficMonitor;
    }

    public async Task StartAsync(int port, CancellationToken cancellationToken) {
        _apiKey = VpnApiAuth.GetOrGenerateApiKey();

        var builder = WebApplication.CreateBuilder();
        _app = builder.Build();

        // Middleware для проверки API ключа (для всех запросов)
        _app.Use(async (context, next) => {
            // Health check пропускаем без аутентификации
            if (context.Request.Path == "/api/health") {
                await next();
                return;
            }

            // Проверяем API ключ в заголовке
            var providedKey = context.Request.Headers["X-API-Key"].FirstOrDefault();

            if (string.IsNullOrEmpty(providedKey) || providedKey != _apiKey) {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new {
                    error = "Unauthorized",
                    message = "Valid API key required"
                });
                return;
            }

            await next();
        });

        // 📊 GET /api/stats - статистика сервера
        _app.MapGet("/api/stats", () => _server.GetServerStats());

        // 👥 GET /api/clients - список клиентов
        _app.MapGet("/api/clients", () => _server.GetAllClients());

        // 🔨 DELETE /api/clients/{id} - отключение клиента
        _app.MapDelete("/api/clients/{id}", (string id) => {
            var result = _server.KickClient(id);
            return result
                ? Results.Ok(new { success = true, message = $"Client {id} kicked" })
                : Results.NotFound(new { success = false, message = $"Client {id} not found" });
        });

        // 🚫 POST /api/clients/{id}/ban - блокировка клиента
        _app.MapPost("/api/clients/{id}/ban", (string id) => {
            var result = _server.BanClient(id);
            return result
                ? Results.Ok(new { success = true, message = $"Client {id} banned" })
                : Results.NotFound(new { success = false, message = $"Client {id} not found" });
        });

        // 🛑 POST /api/server/stop - остановка сервера
        _app.MapPost("/api/server/stop", async () => {
            await _server.StopAsync();
            return Results.Ok(new { success = true, message = "Server stopping..." });
        });

        // ▶️ POST /api/server/start - запуск сервера
        _app.MapPost("/api/server/start", async () => {
            await _server.StartAsync();
            return Results.Ok(new { success = true, message = "Server starting..." });
        });

        // 💚 GET /api/health - health check (без аутентификации)
        _app.MapGet("/api/health", () => new {
            status = "healthy",
            activeClients = _server.ActiveClients,
            timestamp = DateTime.UtcNow
        });

        Console.WriteLine($"\n✅ VPN API запущен на порту {port}");
        Console.WriteLine($"🔑 API Key: {_apiKey}");
        Console.WriteLine($"📡 Endpoint: http://0.0.0.0:{port}/api/");
        Console.WriteLine($"💚 Health check: http://0.0.0.0:{port}/api/health\n");

        await _app.RunAsync($"http://0.0.0.0:{port}");
    }

    public async Task StopAsync() {
        if (_app != null) {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}