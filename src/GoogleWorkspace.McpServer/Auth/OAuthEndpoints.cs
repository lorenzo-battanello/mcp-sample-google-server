using System.Net;

namespace GoogleWorkspace.McpServer.Auth;

public static class OAuthEndpoints
{
    public static void MapOAuthEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/auth");

        // Headers required (X-Api-Key, X-User-Id). Returns the Google consent URL.
        g.MapGet("/login", (HttpContext ctx, IGoogleOAuthService oauth) =>
            Results.Ok(new { authorizationUrl = oauth.BuildLoginUrl((string)ctx.Items[RequestIdentity.ItemKey]!) }));

        // Headers required.
        g.MapGet("/status", async (HttpContext ctx, IGoogleOAuthService oauth, CancellationToken ct) =>
        {
            var user = (string)ctx.Items[RequestIdentity.ItemKey]!;
            return await oauth.IsAuthorizedAsync(user, ct)
                ? Results.Ok(new { authorized = true })
                : Results.Ok(new { authorized = false, loginUrl = oauth.BuildLoginUrl(user) });
        });

        // Browser redirect from Google; no headers. Identity is bound via the one-time state.
        g.MapGet("/callback", async (string? code, string? state, string? error, IGoogleOAuthService oauth, CancellationToken ct) =>
        {
            if (!string.IsNullOrEmpty(error)) return Page(400, "Authorization was denied.");
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state)) return Page(400, "Missing code or state.");
            try
            {
                await oauth.CompleteLoginAsync(code, state, ct);
                return Page(200, "Google account connected. You can close this window.");
            }
            catch (InvalidOperationException ex) { return Page(400, ex.Message); }
        });
    }

    private static IResult Page(int status, string message) =>
        Results.Content($"<!doctype html><meta charset=utf-8><p>{WebUtility.HtmlEncode(message)}</p>", "text/html", null, status);
}
