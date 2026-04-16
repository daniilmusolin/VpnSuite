using System.Net;
using System.Net.Sockets;
using VpnCore.Cryptography;
using VpnCore.Models;
using VpnCore.Protocols;

namespace VpnServer;

public sealed class ClientSession : IDisposable {
    private readonly string _clientId;
    private readonly IPEndPoint _remoteEndpoint;
    private readonly ServerConfig _config;
    private readonly UdpClient _udpServer;
    private AesGcmEncryption? _encryption;
    private NoiseProtocol? _noise;
    private string _virtualIp = "";
    private DateTime _lastActivity;
    private bool _isAuthenticated;
    private bool _disposed;
    private readonly object _sendLock = new();
    private long _bytesSent;
    private long _bytesReceived;
    private long _packetsSent;
    private long _packetsReceived;

    public event Action? OnAuthenticated;
    public event Action<VpnPacket>? OnDataReceived;
    public event Action<string>? OnDisconnected;
    public event Action<string>? OnError;

    public string ClientId => _clientId;
    public IPEndPoint? RemoteEndpoint => _remoteEndpoint;
    public string VirtualIp => _virtualIp;
    public bool IsAuthenticated => _isAuthenticated;
    public DateTime LastActivity => _lastActivity;
    public long BytesSent => _bytesSent;
    public long BytesReceived => _bytesReceived;
    public long PacketsSent => _packetsSent;
    public long PacketsReceived => _packetsReceived;

    public ClientSession(string clientId, IPEndPoint remoteEndpoint, ServerConfig config, UdpClient udpServer) {
        _clientId = clientId;
        _remoteEndpoint = remoteEndpoint;
        _config = config;
        _udpServer = udpServer;
        _lastActivity = DateTime.UtcNow;
        _noise = new NoiseProtocol();
    }

    public async Task<bool> HandleHandshakeAsync(byte[] handshakeData) {
        try {
            var response = _noise!.RespondToHandshake(handshakeData);
            await SendRawAsync(response);
            var sessionKey = _noise.GetSessionKey();
            _encryption = new AesGcmEncryption(sessionKey);
            _isAuthenticated = true;
            _lastActivity = DateTime.UtcNow;
            OnAuthenticated?.Invoke();
            return true;
        } catch (Exception ex) {
            OnError?.Invoke($"Handshake failed: {ex.Message}");
            return false;
        }
    }

    public byte[]? Decrypt(byte[] encrypted) {
        if (!_isAuthenticated || _encryption == null)
            return null;

        try {
            var decrypted = _encryption.Decrypt(encrypted);
            _lastActivity = DateTime.UtcNow;
            Interlocked.Add(ref _bytesReceived, decrypted.Length);
            Interlocked.Increment(ref _packetsReceived);
            return decrypted;
        } catch (Exception ex) {
            OnError?.Invoke($"Decryption failed: {ex.Message}");
            return null;
        }
    }

    public async Task ProcessPacketAsync(byte[] data) {
        try {
            var packet = VpnPacket.Deserialize(data);
            switch (packet.Type) {
                case PacketType.Data:
                    OnDataReceived?.Invoke(packet);
                    break;
                case PacketType.KeepAlive:
                    _lastActivity = DateTime.UtcNow;
                    break;
                case PacketType.Ping:
                    var pongPacket = new VpnPacket(PacketType.Pong, packet.Data);
                    await SendPacketAsync(pongPacket);
                    break;
                case PacketType.Disconnect:
                    await DisconnectAsync("Client requested disconnect");
                    break;
                case PacketType.Error:
                    var errorMsg = System.Text.Encoding.UTF8.GetString(packet.Data);
                    OnError?.Invoke($"Client error: {errorMsg}");
                    break;
            }
        } catch (Exception ex) {
            OnError?.Invoke($"Packet processing failed: {ex.Message}");
        }
    }

    public async Task SendPacketAsync(VpnPacket packet) {
        if (!_isAuthenticated)
            throw new InvalidOperationException("Client not authenticated");
        var data = packet.Serialize();
        await SendAsync(data);
    }

    public async Task SendAsync(byte[] data) {
        if (!_isAuthenticated)
            return;

        lock (_sendLock) {
            var encrypted = _encryption!.Encrypt(data);
            _ = SendRawAsync(encrypted);
            Interlocked.Add(ref _bytesSent, data.Length);
            Interlocked.Increment(ref _packetsSent);
            _lastActivity = DateTime.UtcNow;
        }
        await Task.CompletedTask;
    }

    private async Task SendRawAsync(byte[] data) {
        try {
            await _udpServer.SendAsync(data, data.Length, _remoteEndpoint);
        } catch (Exception ex) {
            OnError?.Invoke($"Send failed: {ex.Message}");
        }
    }

    public void SetVirtualIp(string ip) => _virtualIp = ip;

    public ClientStatistics GetStatistics() => new() {
        ClientId = _clientId,
        RemoteEndpoint = _remoteEndpoint.ToString(),
        VirtualIp = _virtualIp,
        BytesSent = _bytesSent,
        BytesReceived = _bytesReceived,
        PacketsSent = _packetsSent,
        PacketsReceived = _packetsReceived,
        IsAuthenticated = _isAuthenticated,
        LastActivity = _lastActivity
    };

    public async Task DisconnectAsync(string reason) {
        if (!_isAuthenticated)
            return;

        _isAuthenticated = false;
        var disconnectPacket = new VpnPacket(PacketType.Disconnect,
            System.Text.Encoding.UTF8.GetBytes(reason));
        await SendPacketAsync(disconnectPacket);
        OnDisconnected?.Invoke(reason);
    }

    public void Dispose() {
        if (!_disposed) {
            _encryption?.Dispose();
            _noise?.Dispose();
            _disposed = true;
        }
    }
}

public class ClientStatistics {
    public string ClientId { get; set; } = "";
    public string RemoteEndpoint { get; set; } = "";
    public string VirtualIp { get; set; } = "";
    public long BytesSent { get; set; }
    public long BytesReceived { get; set; }
    public long PacketsSent { get; set; }
    public long PacketsReceived { get; set; }
    public bool IsAuthenticated { get; set; }
    public DateTime LastActivity { get; set; }
}