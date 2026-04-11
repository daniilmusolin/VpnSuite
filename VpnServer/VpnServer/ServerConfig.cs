namespace VpnServer {
    /// <summary>
    /// Конфигурация VPN сервера
    /// </summary>
    public class ServerConfig {
        // Сетевые настройки
        public string ListenAddress { get; set; } = "0.0.0.0";
        public int ListenPort { get; set; } = 51820;
        public string VirtualNetwork { get; set; } = "10.8.0.0";
        public int VirtualNetworkMask { get; set; } = 24;

        // Лимиты
        public int MaxClients { get; set; } = 100;
        public int MaxPacketSize { get; set; } = 1400;
        public int Mtu { get; set; } = 1400;

        // Таймауты
        public int HandshakeTimeoutSeconds { get; set; } = 10;
        public int KeepAliveIntervalSeconds { get; set; } = 25;
        public int SessionTimeoutSeconds { get; set; } = 300;

        // Безопасность
        public string CipherSuite { get; set; } = "AES-256-GCM";
        public string HandshakeProtocol { get; set; } = "Noise_IK";
        public bool RequireAuthentication { get; set; } = true;

        // Логирование
        public bool EnableDetailedLogging { get; set; } = false;
        public string LogLevel { get; set; } = "Info";

        // Дополнительно
        public bool EnableNatTraversal { get; set; } = true;
        public bool EnableCompression { get; set; } = false;

        public void Validate() {
            if (ListenPort < 1 || ListenPort > 65535)
                throw new InvalidOperationException("Invalid listen port");

            if (MaxClients < 1 || MaxClients > 10000)
                throw new InvalidOperationException("Max clients must be between 1 and 10000");

            if (Mtu < 576 || Mtu > 9000)
                throw new InvalidOperationException("MTU must be between 576 and 9000");

            if (SessionTimeoutSeconds < 10)
                throw new InvalidOperationException("Session timeout must be at least 10 seconds");
        }
    }
}