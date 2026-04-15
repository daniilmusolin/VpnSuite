namespace VpnClient.Models {
    public class VpnConfig {
        public string ServerAddress { get; set; } = "127.0.0.1";
        public int ServerPort { get; set; } = 51820;
        public int Mtu { get; set; } = 1400;
        public int KeepAliveInterval { get; set; } = 25;
        public int HandshakeTimeout { get; set; } = 5000;
        public bool AutoReconnect { get; set; } = true;
    }
}