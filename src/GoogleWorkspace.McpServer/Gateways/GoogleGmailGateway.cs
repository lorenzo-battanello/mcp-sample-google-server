using System.Text;
using System.Text.RegularExpressions;
using GoogleWorkspace.McpServer.Auth;
using GoogleWorkspace.McpServer.Contracts;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;

namespace GoogleWorkspace.McpServer.Gateways;

public sealed class GoogleGmailGateway(IGoogleOAuthService oauth) : IGmailGateway
{
    private async Task<GmailService> ServiceAsync(string userId, CancellationToken ct) =>
        new(new BaseClientService.Initializer
        {
            HttpClientInitializer = await oauth.GetCredentialAsync(userId, ct),
            ApplicationName = "Google Workspace",
        });

    public async Task<IReadOnlyList<EmailSummaryDto>> SearchEmailsAsync(string userId, string query, int maxResults, CancellationToken ct)
    {
        using var svc = await ServiceAsync(userId, ct);
        var list = svc.Users.Messages.List("me");
        list.Q = query;
        list.MaxResults = maxResults;
        var res = await list.ExecuteAsync(ct);

        var result = new List<EmailSummaryDto>();
        foreach (var m in res.Messages ?? [])
        {
            var get = svc.Users.Messages.Get("me", m.Id);
            get.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
            get.MetadataHeaders = new[] { "From", "Subject", "Date" };
            result.Add(GmailMapper.ToSummary(await get.ExecuteAsync(ct)));
        }
        return result;
    }

    public async Task<EmailDto> ReadEmailAsync(string userId, string messageId, int maxChars, CancellationToken ct)
    {
        using var svc = await ServiceAsync(userId, ct);
        var get = svc.Users.Messages.Get("me", messageId);
        get.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;
        return GmailMapper.ToEmail(await get.ExecuteAsync(ct), maxChars);
    }
}

public static class GmailMapper
{
    public static EmailSummaryDto ToSummary(Message m) =>
        new(m.Id, Header(m, "From"), Header(m, "Subject"), Header(m, "Date"), m.Snippet);

    public static EmailDto ToEmail(Message m, int maxChars)
    {
        var body = FindBody(m.Payload, "text/plain") ?? StripHtml(FindBody(m.Payload, "text/html")) ?? m.Snippet;
        var (text, truncated) = TextLimits.Cap(body, maxChars);
        return new EmailDto(m.Id, Header(m, "From"), Header(m, "To"), Header(m, "Subject"), Header(m, "Date"), text, truncated);
    }

    private static string? Header(Message m, string name) =>
        m.Payload?.Headers?.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;

    private static string? FindBody(MessagePart? part, string mime)
    {
        if (part is null) return null;
        if (part.MimeType == mime && !string.IsNullOrEmpty(part.Body?.Data)) return Decode(part.Body.Data);
        foreach (var child in part.Parts ?? [])
            if (FindBody(child, mime) is { } found) return found;
        return null;
    }

    private static string Decode(string data)
    {
        var s = data.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }

    private static string? StripHtml(string? html)
    {
        if (html is null) return null;
        var t = Regex.Replace(html, @"<(script|style)[^>]*>.*?</\1>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"<[^>]+>", " ");
        return Regex.Replace(System.Net.WebUtility.HtmlDecode(t), @"\s+", " ").Trim();
    }
}
