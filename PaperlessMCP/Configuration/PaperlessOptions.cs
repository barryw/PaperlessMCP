namespace PaperlessMCP.Configuration;

/// <summary>
/// Configuration options for connecting to the Paperless-ngx API.
/// </summary>
public class PaperlessOptions
{
    public const int DefaultMaxPageSize = 100;

    /// <summary>
    /// Default directory that <c>paperless_documents_export_to_outbox</c> writes to.
    /// </summary>
    public const string DefaultOutboxDirectory = "/home/mcp/outbox";

    /// <summary>
    /// Base URL of the Paperless-ngx instance (e.g., https://docs.example.com).
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// API token for authentication.
    /// </summary>
    public string ApiToken { get; set; } = string.Empty;

    /// <summary>
    /// Maximum page size for paginated requests.
    /// </summary>
    public int MaxPageSize { get; set; } = DefaultMaxPageSize;

    /// <summary>
    /// HTTP request timeout in seconds for calls to the Paperless-ngx API.
    /// Large full-text searches over big libraries can exceed the default.
    /// </summary>
    public int HttpTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Filesystem directory that <c>paperless_documents_export_to_outbox</c> writes exported
    /// files into. Intended to be a directory shared (bind-mounted) with other MCP servers so
    /// they can attach the file by path without the bytes passing through the model context.
    /// </summary>
    public string OutboxDirectory { get; set; } = DefaultOutboxDirectory;
}
