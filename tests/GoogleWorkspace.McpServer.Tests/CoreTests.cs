using GoogleWorkspace.McpServer.Auth;
using GoogleWorkspace.McpServer.Configuration;
using GoogleWorkspace.McpServer.Contracts;
using GoogleWorkspace.McpServer.Gateways;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Gmail.v1.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.DataProtection;

namespace GoogleWorkspace.McpServer.Tests;

public class RequestIdentityTests
{
    [Theory]
    [InlineData(null, "u1")]
    [InlineData("", "u1")]
    [InlineData("wrong", "u1")]
    public void Rejects_bad_api_key(string? key, string user) =>
        Assert.Equal(RequestIdentity.Failure.Unauthorized, RequestIdentity.Validate(key, user, "secret").Failure);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("../etc")]
    public void Rejects_bad_user_id(string? user) =>
        Assert.Equal(RequestIdentity.Failure.BadUserId, RequestIdentity.Validate("secret", user, "secret").Failure);

    [Fact]
    public void Accepts_valid_identity()
    {
        var r = RequestIdentity.Validate("secret", "user@example.com", "secret");
        Assert.Equal(RequestIdentity.Failure.None, r.Failure);
        Assert.Equal("user@example.com", r.UserId);
    }

    [Fact]
    public void Empty_server_key_never_authorizes() =>
        Assert.Equal(RequestIdentity.Failure.Unauthorized, RequestIdentity.Validate("x", "u", "").Failure);

    [Fact]
    public async Task Middleware_accepts_generic_user_id_header()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/mcp";
        context.Request.Headers["X-Api-Key"] = "test-key";
        context.Request.Headers["X-User-Id"] = "user-1";
        var called = false;
        var middleware = new RequestIdentityMiddleware(ctx =>
        {
            called = true;
            Assert.Equal("user-1", ctx.Items[RequestIdentity.ItemKey]);
            return Task.CompletedTask;
        }, new AppOptions { ApiKey = "test-key" });

        await middleware.InvokeAsync(context);

        Assert.True(called);
    }

    [Fact]
    public async Task Middleware_rejects_missing_user_id_header()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/mcp";
        context.Request.Headers["X-Api-Key"] = "test-key";
        context.Response.Body = new MemoryStream();
        var middleware = new RequestIdentityMiddleware(_ => throw new InvalidOperationException(),
            new AppOptions { ApiKey = "test-key" });

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }
}

public class FileTokenStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private FileTokenStore Create() => new(_dir, new EphemeralDataProtectionProvider());

    [Fact]
    public async Task Round_trips_per_user_and_encrypts_on_disk()
    {
        var store = Create();
        await store.SaveRefreshTokenAsync("a", "refresh-A");
        await store.SaveRefreshTokenAsync("b", "refresh-B");

        Assert.Equal("refresh-A", await store.GetRefreshTokenAsync("a"));
        Assert.Equal("refresh-B", await store.GetRefreshTokenAsync("b"));
        Assert.Null(await store.GetRefreshTokenAsync("c"));
        foreach (var f in Directory.GetFiles(_dir))
            Assert.DoesNotContain("refresh-", await File.ReadAllTextAsync(f));
    }

    [Fact]
    public async Task Delete_removes_token_and_unreadable_token_is_null()
    {
        var store = Create();
        await store.SaveRefreshTokenAsync("a", "r");
        await store.DeleteAsync("a");
        Assert.Null(await store.GetRefreshTokenAsync("a"));

        await store.SaveRefreshTokenAsync("a", "r");
        var other = new FileTokenStore(_dir, new EphemeralDataProtectionProvider()); // different keys
        Assert.Null(await other.GetRefreshTokenAsync("a"));
    }

    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
}

public class MapperTests
{
    private static string B64(string s) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    [Fact]
    public void Gmail_prefers_plain_text_part_and_caps()
    {
        var msg = new Message
        {
            Id = "m1",
            Payload = new MessagePart
            {
                MimeType = "multipart/alternative",
                Headers = [new MessagePartHeader { Name = "subject", Value = "Hi" }, new MessagePartHeader { Name = "From", Value = "a@b.c" }],
                Parts =
                [
                    new MessagePart { MimeType = "text/html", Body = new MessagePartBody { Data = B64("<p>html</p>") } },
                    new MessagePart { MimeType = "text/plain", Body = new MessagePartBody { Data = B64("0123456789") } },
                ],
            },
        };
        var dto = GmailMapper.ToEmail(msg, 4);
        Assert.Equal("0123", dto.Body);
        Assert.True(dto.Truncated);
        Assert.Equal("Hi", dto.Subject);
        Assert.Equal("a@b.c", dto.From);
    }

    [Fact]
    public void Gmail_falls_back_to_stripped_html()
    {
        var msg = new Message { Id = "m", Payload = new MessagePart { MimeType = "text/html", Body = new MessagePartBody { Data = B64("<style>x{}</style><p>Hello&nbsp;<b>you</b></p>") } } };
        Assert.Equal("Hello you", GmailMapper.ToEmail(msg, 100).Body.Replace('\u00a0', ' '));
    }

    [Fact]
    public void Calendar_maps_to_google_and_back()
    {
        var start = new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.FromHours(1));
        var ev = CalendarMapper.ToGoogle(new NewEventDto("Standup", start, start.AddHours(1), Attendees: ["x@y.z", " "], TimeZone: "Europe/Rome"));
        Assert.Equal("Standup", ev.Summary);
        Assert.Single(ev.Attendees);
        var dto = CalendarMapper.ToDto(new Event { Id = "e", Summary = "S", Start = new EventDateTime { Date = "2026-01-05" }, End = ev.End });
        Assert.Equal("2026-01-05", dto.Start);
        Assert.NotNull(dto.End);
    }

    [Fact]
    public void Limits_clamp_and_cap()
    {
        Assert.Equal(25, TextLimits.Clamp(1000, 10, 25));
        Assert.Equal(10, TextLimits.Clamp(null, 10, 25));
        Assert.Equal(("ab", true), TextLimits.Cap("abc", 2));
    }
}
