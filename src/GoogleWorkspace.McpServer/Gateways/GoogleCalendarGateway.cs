using GoogleWorkspace.McpServer.Auth;
using GoogleWorkspace.McpServer.Contracts;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;

namespace GoogleWorkspace.McpServer.Gateways;

public sealed class GoogleCalendarGateway(IGoogleOAuthService oauth) : ICalendarGateway
{
    private async Task<CalendarService> ServiceAsync(string userId, CancellationToken ct) =>
        new(new BaseClientService.Initializer
        {
            HttpClientInitializer = await oauth.GetCredentialAsync(userId, ct),
            ApplicationName = "Google Workspace",
        });

    public async Task<IReadOnlyList<CalendarEventDto>> ListEventsAsync(string userId, string calendarId, DateTimeOffset from, DateTimeOffset to, int maxResults, CancellationToken ct)
    {
        using var svc = await ServiceAsync(userId, ct);
        var req = svc.Events.List(calendarId);
        req.TimeMinDateTimeOffset = from;
        req.TimeMaxDateTimeOffset = to;
        req.MaxResults = maxResults;
        req.SingleEvents = true;
        req.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
        var res = await req.ExecuteAsync(ct);
        return (res.Items ?? []).Select(CalendarMapper.ToDto).ToList();
    }

    public async Task<CalendarEventDto> CreateEventAsync(string userId, string calendarId, NewEventDto e, CancellationToken ct)
    {
        using var svc = await ServiceAsync(userId, ct);
        var created = await svc.Events.Insert(CalendarMapper.ToGoogle(e), calendarId).ExecuteAsync(ct);
        return CalendarMapper.ToDto(created);
    }
}

public static class CalendarMapper
{
    public static CalendarEventDto ToDto(Event e) => new(
        e.Id, e.Summary, When(e.Start), When(e.End), e.Location, e.HtmlLink);

    public static Event ToGoogle(NewEventDto e) => new()
    {
        Summary = e.Title,
        Description = e.Description,
        Location = e.Location,
        Start = new EventDateTime { DateTimeDateTimeOffset = e.Start, TimeZone = e.TimeZone },
        End = new EventDateTime { DateTimeDateTimeOffset = e.End, TimeZone = e.TimeZone },
        Attendees = e.Attendees?.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => new EventAttendee { Email = a.Trim() }).ToList(),
    };

    private static string? When(EventDateTime? t) =>
        t?.DateTimeDateTimeOffset?.ToString("o") ?? t?.Date;
}
