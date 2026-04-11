//using System;
//using System.Collections.Generic;
//using System.Net.Http;
//using System.Net.Http.Json;
//using System.Text.Json;
//using System.Threading.Tasks;
//using Microsoft.Extensions.Logging;

//namespace VpnBot {
//    /// <summary>
//    /// Клиент для взаимодействия с VPN сервером
//    /// </summary>
//    public class VpnApiClient {
//        private readonly HttpClient _httpClient;
//        private readonly BotConfig _config;
//        private readonly ILogger<VpnApiClient> _logger;

//        public VpnApiClient(HttpClient httpClient, BotConfig config, ILogger<VpnApiClient> logger) {
//            _httpClient = httpClient;
//            _config = config;
//            _logger = logger;

//            _httpClient.BaseAddress = new Uri($"http://{config.VpnApiHost}:{config.VpnApiPort}");
//            _httpClient.Timeout = TimeSpan.FromSeconds(10);
//        }

//        /// <summary>
//        /// Получение статистики сервера
//        /// </summary>
//        public async Task<ServerStats> GetServerStatsAsync() {
//            try {
//                var response = await _httpClient.GetAsync("/api/stats");
//                response.EnsureSuccessStatusCode();

//                return await response.Content.ReadFromJsonAsync<ServerStats>();
//            } catch (Exception ex) {
//                _logger.LogError(ex, "Failed to get server stats");
//                return new ServerStats { IsRunning = false };
//            }
//        }

//        /// <summary>
//        /// Получение списка активных клиентов
//        /// </summary>
//        public async Task<List<ClientInfo>> GetClientsAsync() {
//            try {
//                var response = await _httpClient.GetAsync("/api/clients");
//                response.EnsureSuccessStatusCode();

//                return await response.Content.ReadFromJsonAsync<List<ClientInfo>>() ?? new List<ClientInfo>();
//            } catch (Exception ex) {
//                _logger.LogError(ex, "Failed to get clients");
//                return new List<ClientInfo>();
//            }
//        }

//        /// <summary>
//        /// Отключение клиента
//        /// </summary>
//        public async Task<bool> KickClientAsync(string clientId) {
//            try {
//                var response = await _httpClient.DeleteAsync($"/api/clients/{clientId}");
//                return response.IsSuccessStatusCode;
//            } catch (Exception ex) {
//                _logger.LogError(ex, $"Failed to kick client {clientId}");
//                return false;
//            }
//        }

//        /// <summary>
//        /// Блокировка клиента
//        /// </summary>
//        public async Task<bool> BanClientAsync(string clientId) {
//            try {
//                var response = await _httpClient.PostAsync($"/api/clients/{clientId}/ban", null);
//                return response.IsSuccessStatusCode;
//            } catch (Exception ex) {
//                _logger.LogError(ex, $"Failed to ban client {clientId}");
//                return false;
//            }
//        }

//        /// <summary>
//        /// Получение логов сервера
//        /// </summary>
//        public async Task<List<LogEntry>> GetLogsAsync(int count = 50) {
//            try {
//                var response = await _httpClient.GetAsync($"/api/logs?count={count}");
//                response.EnsureSuccessStatusCode();

//                return await response.Content.ReadFromJsonAsync<List<LogEntry>>() ?? new List<LogEntry>();
//            } catch (Exception ex) {
//                _logger.LogError(ex, "Failed to get logs");
//                return new List<LogEntry>();
//            }
//        }
//    }

//    public class ServerStats {
//        public bool IsRunning { get; set; }
//        public int ActiveClients { get; set; }
//        public long TotalBytesSent { get; set; }
//        public long TotalBytesReceived { get; set; }
//        public long CurrentSendSpeed { get; set; }
//        public long CurrentReceiveSpeed { get; set; }
//        public string Uptime { get; set; }
//        public string CipherSuite { get; set; }
//        public DateTime Timestamp { get; set; }
//    }

//    public class ClientInfo {
//        public string ClientId { get; set; }
//        public string RemoteEndpoint { get; set; }
//        public string VirtualIp { get; set; }
//        public long BytesSent { get; set; }
//        public long BytesReceived { get; set; }
//        public long PacketsSent { get; set; }
//        public long PacketsReceived { get; set; }
//        public DateTime LastActivity { get; set; }
//        public bool IsAuthenticated { get; set; }
//    }

//    public class LogEntry {
//        public DateTime Timestamp { get; set; }
//        public string Level { get; set; }
//        public string Message { get; set; }
//        public string Source { get; set; }
//    }
//}