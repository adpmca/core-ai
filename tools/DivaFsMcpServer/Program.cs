using System.Threading.RateLimiting;
using Diva.Tools.Core;
using Diva.Tools.FileSystem;
using Diva.Tools.FileSystem.Abstractions;
using Diva.Tools.FileSystem.Readers;
using Diva.Tools.FileSystem.Writers;
using DivaFsMcpServer;
using DivaFsMcpServer.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

var isHttp = args.Contains("--http") ||
             !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DIVA_FS_MCP_PORT"));

var builder = WebApplication.CreateBuilder(args);

if (isHttp)
    builder.Host.UseWindowsService(opts => opts.ServiceName = "DivaFsMcpServer");

builder.Services.Configure<FileSystemOptions>(
    builder.Configuration.GetSection(FileSystemOptions.SectionName));
builder.Services.AddSingleton<IValidateOptions<FileSystemOptions>, FileSystemOptionsValidator>();
builder.Services.Configure<StandaloneJwtOptions>(
    builder.Configuration.GetSection(StandaloneJwtOptions.SectionName));
builder.Services.AddSingleton<StandaloneTokenService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IFileSystemPathGuard, FileSystemPathGuard>();
builder.Services.AddScoped<IToolFilter, ToolFilter>();
builder.Services.AddScoped<IPdfReader, PdfReader>();
builder.Services.AddScoped<IImageReader, ImageReader>();
builder.Services.AddScoped<IOfficeReader, OfficeReader>();
builder.Services.AddScoped<IOfficeWriter, OfficeWriter>();
builder.Services.AddTransient<StandaloneAuthMiddleware>();

var mcpBuilder = builder.Services
    .AddMcpServer(opts => opts.ServerInfo = new() { Name = "diva-filesystem", Version = "1.0" })
    .WithDivaMcpTools<FileSystemMcpTools>();

if (isHttp)
{
    mcpBuilder.WithHttpTransport();

    var rateLimit = builder.Configuration.GetValue<int>("FileSystem:RateLimitPerMinute", 120);
    if (rateLimit > 0)
    {
        builder.Services.AddRateLimiter(opts =>
        {
            opts.AddSlidingWindowLimiter("mcp", policy =>
            {
                policy.PermitLimit = rateLimit;
                policy.Window = TimeSpan.FromMinutes(1);
                policy.SegmentsPerWindow = 6;
                policy.QueueLimit = 10;
                policy.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });
            opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        });
    }
}
else
    mcpBuilder.WithStdioServerTransport();

var app = builder.Build();

if (isHttp)
{
    var rateLimit = app.Configuration.GetValue<int>("FileSystem:RateLimitPerMinute", 120);
    if (rateLimit > 0) app.UseRateLimiter();

    app.UseMiddleware<StandaloneAuthMiddleware>();

    app.MapPost("/auth/token", (
        [FromBody] TokenRequest req,
        IOptions<FileSystemOptions> fsOpts,
        StandaloneTokenService tokenSvc) =>
    {
        if (!tokenSvc.IsEnabled)
            return Results.Problem(
                "JWT not configured — set Jwt:SigningKey in appsettings.json",
                statusCode: 501);

        if (string.IsNullOrEmpty(fsOpts.Value.StandaloneApiKey) ||
            req.ApiKey != fsOpts.Value.StandaloneApiKey)
            return Results.Json(new { error = "invalid_client" }, statusCode: 401);

        var t = tokenSvc.IssueToken();
        return Results.Ok(new
        {
            access_token = t.AccessToken,
            expires_in = t.ExpiresIn,
            token_type = "Bearer"
        });
    });

    var mcpEndpoint = app.MapMcp("/mcp");
    if (rateLimit > 0) mcpEndpoint.RequireRateLimiting("mcp");
}

await app.RunAsync();

record TokenRequest(string ApiKey);

// Exposes Program as public partial class for WebApplicationFactory in tests
public partial class Program { }
