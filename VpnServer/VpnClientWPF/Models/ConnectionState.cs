namespace VpnClientWPF.Models;

public enum ConnectionState {
    Disconnected,
    Connecting,
    Handshaking,
    Connected,
    Disconnecting,
    Error
}