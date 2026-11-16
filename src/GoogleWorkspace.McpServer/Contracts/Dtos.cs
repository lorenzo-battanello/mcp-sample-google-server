namespace GoogleWorkspace.McpServer.Contracts;

// Compact tool DTOs. Independent of Google response shapes.

public sealed record DriveFileDto(string Id, string Name, string Type, string? Modified);

public sealed record FileContentDto(string Id, string Name, string Type, string Content, bool Truncated);

public sealed record EmailSummaryDto(string Id, string? From, string? Subject, string? Date, string? Snippet);

public sealed record EmailDto(string Id, string? From, string? To, string? Subject, string? Date, string Body, bool Truncated);

public sealed record CalendarEventDto(string Id, string? Title, string? Start, string? End, string? Location, string? Link);

public sealed record NewEventDto(
    string Title, DateTimeOffset Start, DateTimeOffset End,
    string? Description = null, string? Location = null, IReadOnlyList<string>? Attendees = null, string? TimeZone = null);

public static class TextLimits
{
    public static int Clamp(int? value, int @default, int max) => Math.Clamp(value ?? @default, 1, max);

    public static (string Text, bool Truncated) Cap(string? text, int maxChars)
    {
        text ??= "";
        return text.Length <= maxChars ? (text, false) : (text[..maxChars], true);
    }
}
