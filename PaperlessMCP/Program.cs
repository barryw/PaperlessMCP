using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using PaperlessMCP.Client;
using PaperlessMCP.Configuration;
using PaperlessMCP.Utils;
using Polly;
using Polly.Extensions.Http;

var useStdio = args.Contains("--stdio");

if (useStdio)
{
    // stdio transport for local usage (Claude Desktop)
    var builder = Host.CreateApplicationBuilder(args);

    ConfigureServices(builder.Services, builder.Configuration);

    builder.Logging.AddConsole(options =>
    {
        options.LogToStandardErrorThreshold = LogLevel.Trace;
    });

    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    await builder.Build().RunAsync();
}
else
{
    // HTTP transport for remote usage
    var builder = WebApplication.CreateBuilder(args);

    ConfigureServices(builder.Services, builder.Configuration);

    builder.Services
        .AddMcpServer()
        .WithHttpTransport(options =>
        {
            // Disable idle timeout completely - sessions should never be killed due to inactivity
            // The SDK's IdleTrackingBackgroundService respects InfiniteTimeSpan to skip idle-based pruning
            options.IdleTimeout = Timeout.InfiniteTimeSpan;
        })
        .WithToolsFromAssembly();

    var app = builder.Build();

    var port = app.Configuration.GetValue<int?>("Mcp:Port")
               ?? (Environment.GetEnvironmentVariable("MCP_PORT") is string portStr && int.TryParse(portStr, out var p) ? p : 5000);
    var relaxAcceptHeader = GetBool(app.Configuration, "Mcp:RelaxAcceptHeader", "MCP_RELAX_ACCEPT_HEADER", defaultValue: false);

    if (relaxAcceptHeader)
    {
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsPost(context.Request.Method) &&
                context.Request.Path.StartsWithSegments("/mcp") &&
                McpAcceptHeaderCompatibility.EnsureStreamableHttpAcceptHeader(context.Request))
            {
                app.Logger.LogDebug(
                    "Normalized MCP Accept header for compatibility with clients that cannot send both required media types.");
            }

            await next().ConfigureAwait(false);
        });
    }

    app.MapMcp("/mcp");

    app.Logger.LogInformation("PaperlessMCP server starting on port {Port} with infinite session timeout", port);
    app.Logger.LogInformation("MCP endpoint available at: http://localhost:{Port}/mcp", port);

    await app.RunAsync($"http://0.0.0.0:{port}");
}

bool GetBool(IConfiguration configuration, string key, string environmentVariable, bool defaultValue)
{
    var value = Environment.GetEnvironmentVariable(environmentVariable) ?? configuration.GetValue<string>(key);
    return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
}

void ConfigureServices(IServiceCollection services, IConfiguration configuration)
{
    // Configuration
    services.Configure<PaperlessOptions>(options =>
    {
        // Environment variables take precedence (support both naming conventions)
        options.BaseUrl = Environment.GetEnvironmentVariable("PAPERLESS_BASE_URL")
                          ?? Environment.GetEnvironmentVariable("PAPERLESS_URL")
                          ?? configuration.GetValue<string>("Paperless:BaseUrl")
                          ?? throw new InvalidOperationException("PAPERLESS_BASE_URL or PAPERLESS_URL is required");

        options.ApiToken = Environment.GetEnvironmentVariable("PAPERLESS_API_TOKEN")
                           ?? Environment.GetEnvironmentVariable("PAPERLESS_TOKEN")
                           ?? configuration.GetValue<string>("Paperless:ApiToken")
                           ?? throw new InvalidOperationException("PAPERLESS_API_TOKEN or PAPERLESS_TOKEN is required");

        var configuredMaxPageSize = configuration.GetValue<int?>("Paperless:MaxPageSize")
                                    ?? PaperlessOptions.DefaultMaxPageSize;
        var fallbackMaxPageSize = configuredMaxPageSize > 0
            ? configuredMaxPageSize
            : PaperlessOptions.DefaultMaxPageSize;
        options.MaxPageSize = ParsingHelpers.ParsePositiveInt(
            Environment.GetEnvironmentVariable("MAX_PAGE_SIZE"),
            fallbackMaxPageSize);

        options.HttpTimeoutSeconds = ParsingHelpers.ParsePositiveInt(
            Environment.GetEnvironmentVariable("HTTP_TIMEOUT_SECONDS"),
            configuration.GetValue<int?>("Paperless:HttpTimeoutSeconds") ?? 30);

        options.OutboxDirectory = Environment.GetEnvironmentVariable("PAPERLESS_OUTBOX_DIR")
                                  ?? Environment.GetEnvironmentVariable("OUTBOX_DIR")
                                  ?? configuration.GetValue<string>("Paperless:OutboxDirectory")
                                  ?? PaperlessOptions.DefaultOutboxDirectory;
    });

    // Configure retry policy for transient errors
    var retryPolicy = HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

    // HttpClient for Paperless API
    services.AddHttpClient<PaperlessClient>((sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<PaperlessOptions>>().Value;
        client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Add("Accept", "application/json; version=9");
        client.Timeout = TimeSpan.FromSeconds(options.HttpTimeoutSeconds);
    })
    .AddHttpMessageHandler<PaperlessAuthHandler>()
    .AddPolicyHandler(retryPolicy);

    services.AddTransient<PaperlessAuthHandler>();
}
