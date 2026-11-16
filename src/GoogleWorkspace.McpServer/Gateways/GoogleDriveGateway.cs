using GoogleWorkspace.McpServer.Auth;
using GoogleWorkspace.McpServer.Contracts;
using Google.Apis.Drive.v3;
using Google.Apis.Services;

namespace GoogleWorkspace.McpServer.Gateways;

public sealed class GoogleDriveGateway(IGoogleOAuthService oauth) : IDriveGateway
{
    private const long MaxDownloadBytes = 5 * 1024 * 1024;

    private async Task<DriveService> ServiceAsync(string userId, CancellationToken ct) =>
        new(new BaseClientService.Initializer
        {
            HttpClientInitializer = await oauth.GetCredentialAsync(userId, ct),
            ApplicationName = "Google Workspace",
        });

    public async Task<IReadOnlyList<DriveFileDto>> SearchFilesAsync(string userId, string query, int maxResults, CancellationToken ct)
    {
        using var svc = await ServiceAsync(userId, ct);
        var escaped = query.Replace("\\", "\\\\").Replace("'", "\\'");
        var req = svc.Files.List();
        req.Q = $"(name contains '{escaped}' or fullText contains '{escaped}') and trashed = false";
        req.PageSize = maxResults;
        req.OrderBy = "modifiedTime desc";
        req.Fields = "files(id,name,mimeType,modifiedTime)";
        var res = await req.ExecuteAsync(ct);
        return (res.Files ?? []).Select(f => new DriveFileDto(f.Id, f.Name, f.MimeType, f.ModifiedTimeDateTimeOffset?.ToString("o"))).ToList();
    }

    public async Task<FileContentDto> ReadFileAsync(string userId, string fileId, int maxChars, CancellationToken ct)
    {
        using var svc = await ServiceAsync(userId, ct);
        var get = svc.Files.Get(fileId);
        get.Fields = "id,name,mimeType,size";
        var meta = await get.ExecuteAsync(ct);

        using var ms = new MemoryStream();
        var exportMime = ExportMimeType(meta.MimeType);
        if (exportMime is not null)
        {
            await svc.Files.Export(fileId, exportMime).DownloadAsync(ms, ct);
        }
        else if (IsTextual(meta.MimeType))
        {
            if (meta.Size > MaxDownloadBytes)
                throw new InvalidOperationException("File too large to read.");
            await svc.Files.Get(fileId).DownloadAsync(ms, ct);
        }
        else
        {
            throw new InvalidOperationException($"Unsupported file type: {meta.MimeType}.");
        }

        var (text, truncated) = TextLimits.Cap(System.Text.Encoding.UTF8.GetString(ms.ToArray()), maxChars);
        return new FileContentDto(meta.Id, meta.Name, meta.MimeType, text, truncated);
    }

    public static string? ExportMimeType(string mime) => mime switch
    {
        "application/vnd.google-apps.document" => "text/plain",
        "application/vnd.google-apps.presentation" => "text/plain",
        "application/vnd.google-apps.spreadsheet" => "text/csv",
        _ => null,
    };

    public static bool IsTextual(string mime) =>
        mime.StartsWith("text/", StringComparison.Ordinal) || mime is "application/json" or "application/xml";
}
