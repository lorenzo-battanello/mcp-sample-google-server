using GoogleWorkspace.McpServer.Auth;
using GoogleWorkspace.McpServer.Configuration;
using GoogleWorkspace.McpServer.Gateways;
using Microsoft.AspNetCore.DataProtection;
using GoogleWorkspace.McpServer.Tools;

DotNetEnv.Env.TraversePath().NoClobber().Load();

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();
if (builder.Environment.IsDevelopment()) builder.Configuration.AddUserSecrets<Program>(optional: true);

var options = AppOptions.From(builder.Configuration);
var missing = options.MissingSettings().ToList();
if (missing.Count > 0)
    throw new InvalidOperationException($"Missing required settings: {string.Join(", ", missing)}");

var storePath = Path.GetFullPath(options.TokenStorePath);

builder.Services.AddSingleton(options);
builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();
builder.Services.AddDataProtection()
    .SetApplicationName("GoogleWorkspace.McpServer")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(storePath, "keys")));
builder.Services.AddSingleton<ITokenStore>(sp =>
    new FileTokenStore(Path.Combine(storePath, "tokens"), sp.GetRequiredService<IDataProtectionProvider>()));
builder.Services.AddSingleton<IGoogleOAuthService, GoogleOAuthService>();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<ToolGuard>();
builder.Services.AddScoped<IDriveGateway, GoogleDriveGateway>();
builder.Services.AddScoped<IGmailGateway, GoogleGmailGateway>();
builder.Services.AddScoped<ICalendarGateway, GoogleCalendarGateway>();

builder.Services.AddMcpServer()
    .WithHttpTransport(o => o.Stateless = true)
    .WithToolsFromAssembly();

var app = builder.Build();
app.UseMiddleware<RequestIdentityMiddleware>();
app.MapOAuthEndpoints();
app.MapMcp("/mcp");
app.Run();

public partial class Program;
