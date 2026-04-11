using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace VpnCore.Utils {
    /// <summary>
    /// Вспомогательные утилиты для работы с сетью
    /// </summary>
    public static class NetworkUtils {
        private static readonly Logger _logger = Logger.Instance;

        /// <summary>
        /// Получение локального IP адреса
        /// </summary>
        public static IPAddress GetLocalIpAddress() {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            var endPoint = socket.LocalEndPoint as IPEndPoint;
            return endPoint?.Address ?? IPAddress.Loopback;
        }

        /// <summary>
        /// Получение всех локальных IP адресов
        /// </summary>
        public static IPAddress[] GetAllLocalIpAddresses() {
            var hostName = Dns.GetHostName();
            var hostEntry = Dns.GetHostEntry(hostName);

            return hostEntry.AddressList
                .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork)
                .ToArray();
        }

        /// <summary>
        /// Проверка доступности порта
        /// </summary>
        public static bool IsPortAvailable(int port) {
            var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
            var tcpConnections = ipGlobalProperties.GetActiveTcpConnections();
            var tcpListeners = ipGlobalProperties.GetActiveTcpListeners();
            var udpListeners = ipGlobalProperties.GetActiveUdpListeners();

            return !tcpConnections.Any(c => c.LocalEndPoint.Port == port) &&
                   !tcpListeners.Any(l => l.Port == port) &&
                   !udpListeners.Any(l => l.Port == port);
        }

        /// <summary>
        /// Поиск свободного порта
        /// </summary>
        public static int FindFreePort(int startPort = 10000, int endPort = 65535) {
            for (int port = startPort; port <= endPort; port++) {
                if (IsPortAvailable(port))
                    return port;
            }
            throw new InvalidOperationException("No free ports available");
        }

        /// <summary>
        /// Проверка доступности хоста (ping)
        /// </summary>
        public static async Task<bool> PingHostAsync(string host, int timeoutMs = 3000) {
            try {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(host, timeoutMs);
                return reply.Status == IPStatus.Success;
            } catch {
                return false;
            }
        }

        /// <summary>
        /// Получение MTU для интерфейса
        /// </summary>
        public static int GetMtu(IPAddress address) {
            try {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();

                foreach (var ni in interfaces) {
                    var properties = ni.GetIPProperties();
                    var unicast = properties.UnicastAddresses
                        .FirstOrDefault(u => u.Address.Equals(address));

                    if (unicast != null)
                        return ni.GetIPProperties().GetIPv4Properties()?.Mtu ?? 1500;
                }
            } catch (Exception ex) {
                _logger.Error($"Failed to get MTU: {ex.Message}");
            }

            return 1500; // Значение по умолчанию для Ethernet
        }

        /// <summary>
        /// Преобразование IP адреса в целое число
        /// </summary>
        public static uint IpToUint(IPAddress ip) {
            var bytes = ip.GetAddressBytes();
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return BitConverter.ToUInt32(bytes, 0);
        }

        /// <summary>
        /// Преобразование целого числа в IP адрес
        /// </summary>
        public static IPAddress UintToIp(uint ip) {
            var bytes = BitConverter.GetBytes(ip);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return new IPAddress(bytes);
        }

        /// <summary>
        /// Проверка, является ли IP адрес локальным
        /// </summary>
        public static bool IsLocalIp(IPAddress ip) {
            var localIps = GetAllLocalIpAddresses();
            return localIps.Contains(ip) || IPAddress.IsLoopback(ip);
        }

        /// <summary>
        /// Получение случайного IP из подсети
        /// </summary>
        public static IPAddress GetRandomIpFromSubnet(IPAddress subnet, int prefixLength) {
            var random = new Random();
            var subnetBytes = subnet.GetAddressBytes();
            var hostBits = 32 - prefixLength;
            var maxHosts = (int)Math.Pow(2, hostBits) - 2;

            var hostNumber = random.Next(1, maxHosts + 1);

            var resultBytes = (byte[])subnetBytes.Clone();
            for (int i = 0; i < hostBits; i++) {
                var byteIndex = 3 - (i / 8);
                var bitIndex = i % 8;
                if ((hostNumber & (1 << i)) != 0)
                    resultBytes[byteIndex] |= (byte)(1 << bitIndex);
            }

            return new IPAddress(resultBytes);
        }
    }
}