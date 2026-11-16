# Google Workspace MCP Server

.NET [Model Context Protocol](https://modelcontextprotocol.io) server, exposed over Streamable HTTP, that gives MCP clients access to Google Workspace (Drive, Gmail, Calendar) through the official `Google.Apis.*` SDKs.

> Verification status: build has 0 warnings and 17/17 tests pass. Before the project rename, a local run confirmed 401 without the API key, `tools/list` returning 6 tools, and an unauthorized tool call returning the login URL. **The real Google OAuth flow and Google API calls have not been verified** (they need real credentials).

## Layout

`GoogleWorkspace.McpServer.slnx`, `src/GoogleWorkspace.McpServer` (net10.0), `tests/GoogleWorkspace.McpServer.Tests`.

## Run

Set the required env vars (below), then run the `src/GoogleWorkspace.McpServer` project. Local redirect URI: `http://localhost:5080/auth/callback`. Values are read from environment variables, `.env`, and user-secrets (Development only).

## Tools

| Service | Tool | Access |
|---|---|---|
| Drive | `search_files`, `read_file` | read-only |
| Gmail | `search_emails`, `read_email` | read-only |
| Calendar | `list_events`, `create_event` | `create_event` is the only write |

Parameters and results: see [the feature doc](docs/features/google-workspace-mcp.md#tools).

## Configuration

No secrets in source.

| Variable | Required | Purpose |
|---|---|---|
| `GOOGLE_CLIENT_ID` | yes | Google OAuth client ID |
| `GOOGLE_CLIENT_SECRET` | yes | Google OAuth client secret |
| `GOOGLE_REDIRECT_URI` | yes | OAuth callback URL registered in Google Cloud |
| `MCP_API_KEY` | yes | Shared key the caller sends as `X-Api-Key` |
| `MCP_TOKEN_STORE_PATH` | no (`./data`) | Tokens in `<path>/tokens`, Data Protection keys in `<path>/keys` |
| `MCP_MAX_TEXT_CHARS` | no (`20000`) | Max text returned by tools |
| `ASPNETCORE_URLS` | no | Listen URLs |

The server refuses to start if any required variable is missing.

Keep credentials in environment variables, a local ignored `.env` file, or user-secrets. Keep token storage outside the repository when using a custom `MCP_TOKEN_STORE_PATH`, and protect both tokens and Data Protection keys. Review files before publishing; ignore rules do not protect files already tracked or forcibly added.

## Calling the server

Connect to `POST /mcp` (Streamable HTTP, stateless), sending:

- `X-Api-Key`: must match `MCP_API_KEY`
- `X-User-Id`: identifies the user whose Google account is used

Existing clients must switch to `X-User-Id`. After the project rename, previously stored encrypted refresh tokens cannot be read with the new Data Protection settings; users must authorize their Google accounts again.

## Docs

- [Feature: Google Workspace MCP](docs/features/google-workspace-mcp.md)
- [ADR 0001](docs/architecture/decisions/0001-google-workspace-mcp-server.md)
