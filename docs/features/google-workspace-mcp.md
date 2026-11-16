# Google Workspace MCP Server

Status: implemented. Build: 0 warnings, 17/17 tests. Before the project rename, a local run verified: 401 without key, `tools/list` returns 6 tools, unauthorized tool call returns login URL. **Not verified:** the real Google OAuth flow and real Google API calls (need real credentials).

## Goal

Let MCP clients read Drive files, read Gmail, and read/create Calendar events on behalf of a user.

## Functional behavior

### Connecting a Google account
The server owns Google OAuth.

| Endpoint | Headers required | Behavior |
|---|---|---|
| `GET /auth/login` | yes | Returns `{authorizationUrl}`. PKCE S256, one-time `state` valid 10 min, `access_type=offline`, `prompt=consent`. |
| `GET /auth/status` | yes | Returns `{authorized:true}` or `{authorized:false, loginUrl}`. |
| `GET /auth/callback?code&state` | no | Browser redirect from Google; `state` identifies the user. Exchanges the code, stores the refresh token, shows a small HTML page. |

"Headers" = `X-Api-Key` and `X-User-Id`. Access tokens are refreshed automatically and cached in memory per user.

### Tools
`?` = optional.

| Tool | Parameters | Result |
|---|---|---|
| `search_files` | `query`, `maxResults?` (default 10, max 25) | `[{Id, Name, Type, Modified}]` |
| `read_file` | `fileId`, `maxChars?` | `{Id, Name, Type, Content, Truncated}`. Docs/Slides as text, Sheets as CSV, text/json/xml as-is; files >5 MB or other types return an error. |
| `search_emails` | `query` (Gmail syntax), `maxResults?` (default 10, max 20) | `[{Id, From, Subject, Date, Snippet}]` |
| `read_email` | `messageId`, `maxChars?` (default 8000) | `{Id, From, To, Subject, Date, Body, Truncated}`. Plain-text part, or stripped HTML. |
| `list_events` | `from?`, `to?`, `maxResults?` (default 10, max 50), `calendarId?` (default `primary`) | `[{Id, Title, Start, End, Location, Link}]`. Default window: now to +7 days. |
| `create_event` | `title`, `start`, `end` (ISO 8601, end > start), `description?`, `location?`, `attendees?[]`, `timeZone?`, `calendarId?` | The created event. The only write tool. |

Text output is capped by `MCP_MAX_TEXT_CHARS` (default 20000).

### OAuth scopes
`drive.readonly`, `gmail.readonly`, `calendar.events`.

## Technical design

- **Transport:** `POST /mcp`, Streamable HTTP, stateless (`ModelContextProtocol.AspNetCore` 2.4.0).
- **Caller auth:** `/mcp`, `/auth/login`, `/auth/status` require `X-Api-Key` and `X-User-Id`. Wrong or missing key → 401. User id must match `[A-Za-z0-9._@:-]{1,128}`, else 400. Implemented by the `RequestIdentity` middleware.
- **Code layout** (`src/GoogleWorkspace.McpServer`, net10.0; tests in `tests/GoogleWorkspace.McpServer.Tests`):
  - `Auth/`: `RequestIdentity`, `ITokenStore` / `FileTokenStore`, `GoogleOAuthService`, `OAuthEndpoints`.
  - `Contracts/Dtos.cs`: own DTOs for tool I/O.
  - `Gateways/`: `IDriveGateway`, `IGmailGateway`, `ICalendarGateway` plus Google implementations. The only place with Google types.
  - `Tools/Tools.cs`: MCP tools. `Configuration/AppOptions.cs`: options.
- **Token storage:** refresh tokens encrypted with ASP.NET Data Protection; one file per user, named by a hash of the user id, in `<MCP_TOKEN_STORE_PATH>/tokens` (keys in `<MCP_TOKEN_STORE_PATH>/keys`). Another store can be used by registering a different `ITokenStore`.
- **Pending login state:** in memory; a restart invalidates in-progress logins.
- **Packages:** `ModelContextProtocol.AspNetCore` 2.4.0, `Google.Apis.Drive.v3`, `Google.Apis.Gmail.v1`, `Google.Apis.Calendar.v3`, `DotNetEnv`.

### Configuration
Loaded from environment variables, `.env`, and user-secrets (Development only). Required: `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`, `GOOGLE_REDIRECT_URI` (local: `http://localhost:5080/auth/callback`), `MCP_API_KEY`; the server refuses to start without them. Optional: `MCP_TOKEN_STORE_PATH` (default `./data`), `MCP_MAX_TEXT_CHARS` (default 20000), `ASPNETCORE_URLS`. No secrets in source.

## Error behavior
- User not authorized: the tool returns an MCP error `Google authorization required. Ask the user to open: <url>`.
- Google rejects the stored refresh token: the server deletes it and returns the same error with a new login URL.
- Other tool errors (e.g. unsupported or >5 MB file in `read_file`) are returned as errors.

## Open questions
- Handling of Google API failures other than rejected refresh tokens was not specified.
- Real Google OAuth and API verification is pending.
