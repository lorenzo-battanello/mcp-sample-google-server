using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace GoogleWorkspace.McpServer.Auth;

/// <summary>Persists one Google refresh token per user. Swappable.</summary>
public interface ITokenStore
{
    Task<string?> GetRefreshTokenAsync(string userId, CancellationToken ct = default);
    Task SaveRefreshTokenAsync(string userId, string refreshToken, CancellationToken ct = default);
    Task DeleteAsync(string userId, CancellationToken ct = default);
}

/// <summary>One Data-Protection-encrypted file per user; file name is a hash of the user id.</summary>
public sealed class FileTokenStore : ITokenStore
{
    private readonly string _dir;
    private readonly IDataProtector _protector;

    public FileTokenStore(string directory, IDataProtectionProvider provider)
    {
        _dir = directory;
        _protector = provider.CreateProtector("GoogleWorkspace.McpServer.RefreshTokens.v1");
        Directory.CreateDirectory(_dir);
    }

    private string PathFor(string userId) =>
        Path.Combine(_dir, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId))) + ".token");

    public async Task<string?> GetRefreshTokenAsync(string userId, CancellationToken ct = default)
    {
        var path = PathFor(userId);
        if (!File.Exists(path)) return null;
        try { return _protector.Unprotect(await File.ReadAllTextAsync(path, ct)); }
        catch (CryptographicException) { return null; } // unreadable (e.g. keys lost) => treat as unauthorized
    }

    public Task SaveRefreshTokenAsync(string userId, string refreshToken, CancellationToken ct = default) =>
        File.WriteAllTextAsync(PathFor(userId), _protector.Protect(refreshToken), ct);

    public Task DeleteAsync(string userId, CancellationToken ct = default)
    {
        File.Delete(PathFor(userId));
        return Task.CompletedTask;
    }
}
