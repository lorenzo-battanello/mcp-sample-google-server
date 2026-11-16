# ADR 0001: Google Workspace MCP server design

Status: Accepted

## Context
MCP clients need agent access to Google Drive, Gmail and Calendar, with minimal write capability and no Google SDK coupling in tool code.

## Decision
- A standalone .NET MCP server over Streamable HTTP, called by clients with `X-Api-Key` and `X-User-Id`.
- The MCP server owns Google OAuth (login/callback/status endpoints), stores refresh tokens encrypted behind `ITokenStore`, and refreshes access tokens automatically.
- Google SDK calls are isolated behind `IDriveGateway`, `IGmailGateway`, `ICalendarGateway`; tools use their own DTOs.
- Least-privilege scopes: `drive.readonly`, `gmail.readonly`, `calendar.events`. Only `create_event` writes.
- Credentials and the API key come from environment variables (`GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`, `GOOGLE_REDIRECT_URI`, `MCP_API_KEY`), never source.

## Consequences
- The backend trusts the server via a shared API key and passes the user ID.
- Gateways and `ITokenStore` keep Google types confined to `Gateways/` and let storage be swapped by registering another `ITokenStore`. The default is `FileTokenStore`: one ASP.NET Data Protection-encrypted file per user.
- The server holds credentials, so the token directory (`tokens` and Data Protection `keys`) must be protected.
- Pending login states are in memory, so a restart invalidates in-progress logins.
- Real Google OAuth and API calls are not yet verified against real credentials.
