using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GoogleWorkspace.McpServer.Auth;

public static class RequestIdentity
{
    public const string ApiKeyHeader = "X-Api-Key";
    public const string UserIdHeader = "X-User-Id";
    public const string ItemKey = "McpServer.UserId";

    private static readonly Regex ValidUserId = new(@"^[A-Za-z0-9._@:\-]{1,128}$", RegexOptions.Compiled);

    public enum Failure { None, Unauthorized, BadUserId }

    public static (Failure Failure, string? UserId) Validate(string? apiKey, string? userId, string expectedApiKey)
    {
        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(expectedApiKey) || !FixedEquals(apiKey, expectedApiKey))
            return (Failure.Unauthorized, null);
        if (userId is null || !ValidUserId.IsMatch(userId))
            return (Failure.BadUserId, null);
        return (Failure.None, userId);
    }

    private static bool FixedEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(a)), SHA256.HashData(Encoding.UTF8.GetBytes(b)));
}

/// <summary>Requires X-Api-Key + X-User-Id on /mcp and /auth/login|status. /auth/callback is browser-facing and exempt.</summary>
public sealed class RequestIdentityMiddleware(RequestDelegate next, Configuration.AppOptions options)
{
    public Task InvokeAsync(HttpContext ctx)
    {
        var path = ctx.Request.Path;
        var protectedPath = path.StartsWithSegments("/mcp") || path.StartsWithSegments("/auth/login") || path.StartsWithSegments("/auth/status");
        if (!protectedPath) return next(ctx);

        var (failure, userId) = RequestIdentity.Validate(
            ctx.Request.Headers[RequestIdentity.ApiKeyHeader].FirstOrDefault(),
            ctx.Request.Headers[RequestIdentity.UserIdHeader].FirstOrDefault(),
            options.ApiKey);

        switch (failure)
        {
            case RequestIdentity.Failure.Unauthorized:
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return ctx.Response.WriteAsync("Invalid or missing X-Api-Key.");
            case RequestIdentity.Failure.BadUserId:
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                return ctx.Response.WriteAsync("Invalid or missing X-User-Id.");
        }
        ctx.Items[RequestIdentity.ItemKey] = userId;
        return next(ctx);
    }
}

public interface ICurrentUser { string UserId { get; } }

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string UserId => accessor.HttpContext?.Items[RequestIdentity.ItemKey] as string
        ?? throw new InvalidOperationException("No authenticated user on this request.");
}
