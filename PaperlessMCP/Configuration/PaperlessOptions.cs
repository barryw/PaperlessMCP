namespace PaperlessMCP.Configuration;

/// <summary>
/// Configuration options for connecting to the Paperless-ngx API.
/// </summary>
public class PaperlessOptions
{
    public const int DefaultMaxPageSize = 100;

    /// <summary>
    /// Paperless-ngx REST API version requested via the <c>Accept</c> header.
    /// Paperless-ngx 3.x supports versions 9 and 10; 10 is the current default.
    /// </summary>
    public const string ApiVersion = "10";

    /// <summary>
    /// Full <c>Accept</c> header value for Paperless-ngx API requests.
    /// </summary>
    public const string ApiAcceptHeader = $"application/json; version={ApiVersion}";

    /// <summary>
    /// Default directory that <c>paperless_documents_export_to_outbox</c> writes to.
    /// </summary>
    public const string DefaultOutboxDirectory = "/home/mcp/outbox";

    /// <summary>
    /// Default upper bound (in bytes) on the raw file size that
    /// <c>paperless_documents_download</c> will inline as base64 (12 KB).
    /// </summary>
    public const int DefaultMaxInlineBase64Bytes = 12 * 1024;

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

    /// <summary>
    /// Upper bound (in bytes) on the raw file size that <c>paperless_documents_download</c>
    /// will inline as base64 when <c>returnBase64=true</c>. Larger files must go through
    /// <c>paperless_documents_export_to_outbox</c>. Keep this modest: base64 inflates the
    /// payload by ~4/3 and the whole tool response must fit the model's tool-output budget.
    /// Values of zero or less fall back to <see cref="DefaultMaxInlineBase64Bytes"/>.
    /// </summary>
    public int MaxInlineBase64Bytes { get; set; } = DefaultMaxInlineBase64Bytes;
}
