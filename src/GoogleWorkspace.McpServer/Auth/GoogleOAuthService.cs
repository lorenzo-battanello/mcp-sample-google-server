using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoogleWorkspace.McpServer.Configuration;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;

namespace GoogleWorkspace.McpServer.Auth;

public sealed class AuthorizationRequiredException(string loginUrl)
    : Exception($"Google authorization required. Ask the user to open: {loginUrl}")
{
    public string LoginUrl { get; } = loginUrl;
}

public interface IGoogleOAuthService
{
    string BuildLoginUrl(string userId);
    Task<string> CompleteLoginAsync(string code, string state, CancellationToken ct);
    Task<bool> IsAuthorizedAsync(string userId, CancellationToken ct);
    /// <summary>Returns a credential for the user, or throws <see cref="AuthorizationRequiredException"/>.</summary>
    Task<UserCredential> GetCredentialAsync(string userId, CancellationToken ct);
    Task ForgetAsync(string userId, CancellationToken ct);
}

public sealed class GoogleOAuthService : IGoogleOAuthService
{
    public static readonly string[] Scopes =
    [
        "https://www.googleapis.com/auth/drive.readonly",
        "https://www.googleapis.com/auth/gmail.readonly",
        "https://www.googleapis.com/auth/calendar.events",
    ];

    private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    private sealed record Pending(string UserId, string Verifier, DateTimeOffset Expires);

    private readonly AppOptions _options;
    private readonly ITokenStore _store;
    private readonly IHttpClientFactory _http;
    private readonly GoogleAuthorizationCodeFlow _flow;
    private readonly ConcurrentDictionary<string, Pending> _pending = new();
    private readonly ConcurrentDictionary<string, UserCredential> _credentials = new();

    public GoogleOAuthService(AppOptions options, ITokenStore store, IHttpClientFactory http)
    {
        _options = options;
        _store = store;
        _http = http;
        _flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = options.GoogleClientId, ClientSecret = options.GoogleClientSecret },
            Scopes = Scopes,
        });
    }

    public string BuildLoginUrl(string userId)
    {
        foreach (var (k, v) in _pending.ToArray())
            if (v.Expires < DateTimeOffset.UtcNow) _pending.TryRemove(k, out _);

        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        _pending[state] = new Pending(userId, verifier, DateTimeOffset.UtcNow + StateLifetime);

        var q = new Dictionary<string, string>
        {
            ["client_id"] = _options.GoogleClientId,
            ["redirect_uri"] = _options.GoogleRedirectUri,
            ["response_type"] = "code",
            ["scope"] = string.Join(' ', Scopes),
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        };
        return AuthEndpoint + "?" + string.Join('&', q.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
    }

    public async Task<string> CompleteLoginAsync(string code, string state, CancellationToken ct)
    {
        if (!_pending.TryRemove(state, out var p) || p.Expires < DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Unknown or expired login state. Start the login again.");

        using var resp = await _http.CreateClient().PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _options.GoogleClientId,
            ["client_secret"] = _options.GoogleClientSecret,
            ["redirect_uri"] = _options.GoogleRedirectUri,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["code_verifier"] = p.Verifier,
        }), ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException("Google rejected the authorization code.");

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("refresh_token", out var rt) || string.IsNullOrEmpty(rt.GetString()))
            throw new InvalidOperationException("Google returned no refresh token. Revoke the app's access and retry.");

        await _store.SaveRefreshTokenAsync(p.UserId, rt.GetString()!, ct);
        _credentials.TryRemove(p.UserId, out _);
        return p.UserId;
    }

    public async Task<bool> IsAuthorizedAsync(string userId, CancellationToken ct) =>
        await _store.GetRefreshTokenAsync(userId, ct) is not null;

    public async Task<UserCredential> GetCredentialAsync(string userId, CancellationToken ct)
    {
        if (_credentials.TryGetValue(userId, out var cached)) return cached;
        var refresh = await _store.GetRefreshTokenAsync(userId, ct)
            ?? throw new AuthorizationRequiredException(BuildLoginUrl(userId));
        var cred = new UserCredential(_flow, userId, new TokenResponse { RefreshToken = refresh });
        _credentials[userId] = cred;
        return cred;
    }

    public async Task ForgetAsync(string userId, CancellationToken ct)
    {
        _credentials.TryRemove(userId, out _);
        await _store.DeleteAsync(userId, ct);
    }

    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
