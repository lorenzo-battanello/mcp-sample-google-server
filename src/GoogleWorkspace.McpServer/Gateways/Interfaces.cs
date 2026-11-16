using GoogleWorkspace.McpServer.Contracts;

namespace GoogleWorkspace.McpServer.Gateways;

// Boundary to Google SDKs. Only the implementations in Gateways/Google* know Google types.

public interface IDriveGateway
{
    Task<IReadOnlyList<DriveFileDto>> SearchFilesAsync(string userId, string query, int maxResults, CancellationToken ct);
    Task<FileContentDto> ReadFileAsync(string userId, string fileId, int maxChars, CancellationToken ct);
}

public interface IGmailGateway
{
    Task<IReadOnlyList<EmailSummaryDto>> SearchEmailsAsync(string userId, string query, int maxResults, CancellationToken ct);
    Task<EmailDto> ReadEmailAsync(string userId, string messageId, int maxChars, CancellationToken ct);
}

public interface ICalendarGateway
{
    Task<IReadOnlyList<CalendarEventDto>> ListEventsAsync(string userId, string calendarId, DateTimeOffset from, DateTimeOffset to, int maxResults, CancellationToken ct);
    Task<CalendarEventDto> CreateEventAsync(string userId, string calendarId, NewEventDto newEvent, CancellationToken ct);
}
