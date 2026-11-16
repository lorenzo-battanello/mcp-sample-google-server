namespace GoogleWorkspace.McpServer.Configuration;

/// <summary>Settings read from env vars / .env / user-secrets using the flat key names below.</summary>
public class AppOptions
{
    public string GoogleClientId { get; init; } = "";
    public string GoogleClientSecret { get; init; } = "";
    public string GoogleRedirectUri { get; init; } = "";
    public string ApiKey { get; init; } = "";
    public string TokenStorePath { get; init; } = "./data";

    /// <summary>Hard cap (characters) for any text returned by a tool.</summary>
    public int MaxTextChars { get; init; } = 20000;

    public static AppOptions From(IConfiguration c) => new()
    {
        GoogleClientId = c["GOOGLE_CLIENT_ID"] ?? "",
        GoogleClientSecret = c["GOOGLE_CLIENT_SECRET"] ?? "",
        GoogleRedirectUri = c["GOOGLE_REDIRECT_URI"] ?? "",
        ApiKey = c["MCP_API_KEY"] ?? "",
        TokenStorePath = c["MCP_TOKEN_STORE_PATH"] is { Length: > 0 } p ? p : "./data",
        MaxTextChars = int.TryParse(c["MCP_MAX_TEXT_CHARS"], out var n) && n > 0 ? n : 20000,
    };

    public IEnumerable<string> MissingSettings()
    {
        if (GoogleClientId.Length == 0) yield return "GOOGLE_CLIENT_ID";
        if (GoogleClientSecret.Length == 0) yield return "GOOGLE_CLIENT_SECRET";
        if (GoogleRedirectUri.Length == 0) yield return "GOOGLE_REDIRECT_URI";
        if (ApiKey.Length == 0) yield return "MCP_API_KEY";
    }
}
