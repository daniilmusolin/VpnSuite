using System.Collections.Concurrent;
using System.Text.Json;

namespace VpnBot;

public class BotUser {
    public long UserId { get; set; }
    public string? Username { get; set; }
    public string? FirstName { get; set; }
    public UserRole Role { get; set; }
    public DateTime AddedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public bool IsActive { get; set; }
    public int CommandCount { get; set; }

    public string DisplayName => !string.IsNullOrEmpty(Username) ? $"@{Username}" : FirstName ?? UserId.ToString();
    public string RoleIcon => Role switch {
        UserRole.Owner => "👑",
        UserRole.Admin => "🛡️",
        UserRole.Viewer => "👁️",
        UserRole.Banned => "🚫",
        _ => "❓"
    };
}

public class UserManager {
    private readonly BotConfig _config;
    private readonly ConcurrentDictionary<long, BotUser> _users;
    private readonly string _usersFilePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public UserManager(BotConfig config) {
        _config = config;
        _users = new ConcurrentDictionary<long, BotUser>();
        _usersFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "users.json");

        LoadUsers().Wait();
        InitializeDefaultUsers();
    }

    private void InitializeDefaultUsers() {
        if (_config.OwnerId > 0 && !_users.ContainsKey(_config.OwnerId)) {
            _users[_config.OwnerId] = new BotUser {
                UserId = _config.OwnerId,
                Role = UserRole.Owner,
                AddedAt = DateTime.UtcNow,
                IsActive = true
            };
        }

        foreach (var adminId in _config.AdminIds) {
            if (!_users.ContainsKey(adminId)) {
                _users[adminId] = new BotUser {
                    UserId = adminId,
                    Role = UserRole.Admin,
                    AddedAt = DateTime.UtcNow,
                    IsActive = true
                };
            }
        }

        foreach (var viewerId in _config.ViewerIds) {
            if (!_users.ContainsKey(viewerId)) {
                _users[viewerId] = new BotUser {
                    UserId = viewerId,
                    Role = UserRole.Viewer,
                    AddedAt = DateTime.UtcNow,
                    IsActive = true
                };
            }
        }
    }

    public UserRole GetUserRole(long userId) => _users.TryGetValue(userId, out var user) ? user.Role : UserRole.Banned;
    public bool CanKick(long userId) => GetUserRole(userId) == UserRole.Owner || GetUserRole(userId) == UserRole.Admin;
    public bool CanBan(long userId) => GetUserRole(userId) == UserRole.Owner;
    public bool CanAddAdmin(long userId) => GetUserRole(userId) == UserRole.Owner;
    public bool IsAuthorized(long userId) => GetUserRole(userId) != UserRole.Banned;

    public async Task<bool> AddUser(long userId, UserRole role, string? username = null) {
        if (_users.ContainsKey(userId)) return false;

        _users[userId] = new BotUser {
            UserId = userId,
            Username = username,
            Role = role,
            AddedAt = DateTime.UtcNow,
            IsActive = true
        };

        await SaveUsers();
        return true;
    }

    public async Task<bool> ChangeRole(long userId, UserRole newRole) {
        if (!_users.TryGetValue(userId, out var user)) return false;
        user.Role = newRole;
        await SaveUsers();
        return true;
    }

    public async Task<bool> RemoveUser(long userId) {
        if (!_users.TryRemove(userId, out _)) return false;
        await SaveUsers();
        return true;
    }

    public List<BotUser> GetAllUsers() => _users.Values.OrderBy(u => u.Role).ThenBy(u => u.AddedAt).ToList();
    public BotUser? GetUser(long userId) => _users.TryGetValue(userId, out var user) ? user : null;

    public void UpdateActivity(long userId) {
        if (_users.TryGetValue(userId, out var user)) {
            user.LastActivityAt = DateTime.UtcNow;
            user.CommandCount++;
        }
    }

    private async Task LoadUsers() {
        if (!File.Exists(_usersFilePath)) return;

        await _fileLock.WaitAsync();
        try {
            var json = await File.ReadAllTextAsync(_usersFilePath);
            var users = JsonSerializer.Deserialize<List<BotUser>>(json);
            if (users != null) {
                foreach (var user in users)
                    _users[user.UserId] = user;
            }
        } finally {
            _fileLock.Release();
        }
    }

    private async Task SaveUsers() {
        await _fileLock.WaitAsync();
        try {
            var users = _users.Values.ToList();
            var json = JsonSerializer.Serialize(users, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_usersFilePath, json);
        } finally {
            _fileLock.Release();
        }
    }
}