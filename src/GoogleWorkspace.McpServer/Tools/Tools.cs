using System.ComponentModel;
using System.Globalization;
using GoogleWorkspace.McpServer.Auth;
using GoogleWorkspace.McpServer.Configuration;
using GoogleWorkspace.McpServer.Contracts;
using GoogleWorkspace.McpServer.Gateways;
using Google;
using Google.Apis.Auth.OAuth2.Responses;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace GoogleWorkspace.McpServer.Tools;

/// <summary>Resolves the caller and converts failures into client-visible MCP errors.</summary>
public sealed class ToolGuard(ICurrentUser user, IGoogleOAuthService oauth)
{
    public string UserId => user.UserId;

    public async Task<T> RunAsync<T>(Func<string, Task<T>> action, CancellationToken ct)
    {
        try { return await action(user.UserId); }
        catch (AuthorizationRequiredException ex) { throw new McpException(ex.Message); }
        catch (TokenResponseException)
        {
            await oauth.ForgetAsync(user.UserId, ct);
            throw new McpException($"Google authorization expired. Ask the user to open: {oauth.BuildLoginUrl(user.UserId)}");
        }
        catch (GoogleApiException ex) { throw new McpException($"Google API error {(int)ex.HttpStatusCode}: {ex.Error?.Message ?? ex.Message}"); }
        catch (InvalidOperationException ex) { throw new McpException(ex.Message); }
    }

    public static DateTimeOffset ParseDate(string value, string name) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)
            ? d : throw new McpException($"{name} must be ISO 8601, e.g. 2026-01-31T09:00:00+01:00.");
}

[McpServerToolType]
public sealed class DriveTools(ToolGuard guard, IDriveGateway drive, AppOptions options)
{
    [McpServerTool(Name = "search_files", ReadOnly = true), Description("Search Drive files by name/content. Returns id, name, type, modified.")]
    public Task<IReadOnlyList<DriveFileDto>> SearchFiles(
        [Description("Search text")] string query,
        [Description("Max results (default 10, max 25)")] int? maxResults = null,
        CancellationToken ct = default) =>
        guard.RunAsync(u => drive.SearchFilesAsync(u, query, TextLimits.Clamp(maxResults, 10, 25), ct), ct);

    [McpServerTool(Name = "read_file", ReadOnly = true), Description("Read a Drive file as text (Docs/Sheets exported). Output is capped.")]
    public Task<FileContentDto> ReadFile(
        [Description("Drive file id")] string fileId,
        [Description("Max characters (default 20000)")] int? maxChars = null,
        CancellationToken ct = default) =>
        guard.RunAsync(u => drive.ReadFileAsync(u, fileId, TextLimits.Clamp(maxChars, options.MaxTextChars, options.MaxTextChars), ct), ct);
}

[McpServerToolType]
public sealed class GmailTools(ToolGuard guard, IGmailGateway gmail, AppOptions options)
{
    [McpServerTool(Name = "search_emails", ReadOnly = true), Description("Search Gmail (Gmail query syntax). Returns id, from, subject, date, snippet.")]
    public Task<IReadOnlyList<EmailSummaryDto>> SearchEmails(
        [Description("Gmail query, e.g. from:bob is:unread")] string query,
        [Description("Max results (default 10, max 20)")] int? maxResults = null,
        CancellationToken ct = default) =>
        guard.RunAsync(u => gmail.SearchEmailsAsync(u, query, TextLimits.Clamp(maxResults, 10, 20), ct), ct);

    [McpServerTool(Name = "read_email", ReadOnly = true), Description("Read one email's headers and text body. Body is capped.")]
    public Task<EmailDto> ReadEmail(
        [Description("Message id")] string messageId,
        [Description("Max body characters (default 8000)")] int? maxChars = null,
        CancellationToken ct = default) =>
        guard.RunAsync(u => gmail.ReadEmailAsync(u, messageId, TextLimits.Clamp(maxChars, Math.Min(8000, options.MaxTextChars), options.MaxTextChars), ct), ct);
}

[McpServerToolType]
public sealed class CalendarTools(ToolGuard guard, ICalendarGateway calendar)
{
    [McpServerTool(Name = "list_events", ReadOnly = true), Description("List calendar events in a time range (default: next 7 days).")]
    public Task<IReadOnlyList<CalendarEventDto>> ListEvents(
        [Description("Range start, ISO 8601 (default now)")] string? from = null,
        [Description("Range end, ISO 8601 (default from+7d)")] string? to = null,
        [Description("Max results (default 10, max 50)")] int? maxResults = null,
        [Description("Calendar id (default primary)")] string? calendarId = null,
        CancellationToken ct = default)
    {
        var start = from is null ? DateTimeOffset.UtcNow : ToolGuard.ParseDate(from, "from");
        var end = to is null ? start.AddDays(7) : ToolGuard.ParseDate(to, "to");
        return guard.RunAsync(u => calendar.ListEventsAsync(u, calendarId ?? "primary", start, end, TextLimits.Clamp(maxResults, 10, 50), ct), ct);
    }

    [McpServerTool(Name = "create_event", Destructive = false, ReadOnly = false), Description("Create a calendar event. Start/end are ISO 8601 with offset.")]
    public Task<CalendarEventDto> CreateEvent(
        [Description("Event title")] string title,
        [Description("Start, ISO 8601")] string start,
        [Description("End, ISO 8601")] string end,
        [Description("Description")] string? description = null,
        [Description("Location")] string? location = null,
        [Description("Attendee emails")] string[]? attendees = null,
        [Description("IANA time zone, e.g. Europe/Rome")] string? timeZone = null,
        [Description("Calendar id (default primary)")] string? calendarId = null,
        CancellationToken ct = default)
    {
        var s = ToolGuard.ParseDate(start, "start");
        var e = ToolGuard.ParseDate(end, "end");
        if (e <= s) throw new McpException("end must be after start.");
        var ev = new NewEventDto(title, s, e, description, location, attendees, timeZone);
        return guard.RunAsync(u => calendar.CreateEventAsync(u, calendarId ?? "primary", ev, ct), ct);
    }
}
