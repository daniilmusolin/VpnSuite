namespace VpnClient.Models;

public enum ConnectionState {
    Disconnected = 0,
    Connecting = 1,
    Connected = 2,
    Handshaking = 3,
    Disconnecting = 4,
    Error = 5
}