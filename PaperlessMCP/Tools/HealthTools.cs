using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using PaperlessMCP.Client;
using PaperlessMCP.Models.Common;

namespace PaperlessMCP.Tools;

/// <summary>
/// MCP tools for health checks and capability discovery.
/// </summary>
[McpServerToolType]
public static class HealthTools
{
    [McpServerTool(Name = "paperless-ping")]
    [Description("Verify connectivity and authentication with the Paperless-ngx instance. Returns server version if available.")]
    public static async Task<string> Ping(PaperlessClient client)
    {
        var (success, version, error) = await client.PingAsync().ConfigureAwait(false);

        if (success)
        {
            var response = McpResponse<object>.Success(
                new { connected = true, version },
                new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(response);
        }

        var errorResponse = McpErrorResponse.Create(
            ErrorCodes.UpstreamError,
            error ?? "Failed to connect to Paperless instance",
            meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(errorResponse);
    }

    [McpServerTool(Name = "paperless-capabilities")]
    [Description("Return supported API endpoints and detected Paperless-ngx version information.")]
    public static async Task<string> GetCapabilities(PaperlessClient client)
    {
        var (pingSuccess, version, _) = await client.PingAsync().ConfigureAwait(false);
        var (statusSuccess, status, _) = await client.GetStatusAsync().ConfigureAwait(false);

        var capabilities = new
        {
            connected = pingSuccess,
            version,
            endpoints = new
            {
                documents = new
                {
                    search = "/api/documents/",
                    get = "/api/documents/{id}/",
                    upload = "/api/documents/post_document/",
                    update = "/api/documents/{id}/",
                    delete = "/api/documents/{id}/",
                    download = "/api/documents/{id}/download/",
                    preview = "/api/documents/{id}/preview/",
                    thumbnail = "/api/documents/{id}/thumb/",
                    bulk_edit = "/api/documents/bulk_edit/"
                },
                tags = new
                {
                    list = "/api/tags/",
                    get = "/api/tags/{id}/",
                    create = "/api/tags/",
                    update = "/api/tags/{id}/",
                    delete = "/api/tags/{id}/"
                },
                correspondents = new
                {
                    list = "/api/correspondents/",
                    get = "/api/correspondents/{id}/",
                    create = "/api/correspondents/",
                    update = "/api/correspondents/{id}/",
                    delete = "/api/correspondents/{id}/"
                },
                document_types = new
                {
                    list = "/api/document_types/",
                    get = "/api/document_types/{id}/",
                    create = "/api/document_types/",
                    update = "/api/document_types/{id}/",
                    delete = "/api/document_types/{id}/"
                },
                storage_paths = new
                {
                    list = "/api/storage_paths/",
                    get = "/api/storage_paths/{id}/",
                    create = "/api/storage_paths/",
                    update = "/api/storage_paths/{id}/",
                    delete = "/api/storage_paths/{id}/"
                },
                custom_fields = new
                {
                    list = "/api/custom_fields/",
                    get = "/api/custom_fields/{id}/",
                    create = "/api/custom_fields/",
                    update = "/api/custom_fields/{id}/",
                    delete = "/api/custom_fields/{id}/"
                },
                bulk_operations = "/api/bulk_edit_objects/"
            },
            bulk_edit_methods = new[]
            {
                "set_correspondent",
                "set_document_type",
                "set_storage_path",
                "add_tag",
                "remove_tag",
                "modify_tags",
                "modify_custom_fields",
                "delete",
                "reprocess"
            },
            status = statusSuccess ? status?.RootElement : null
        };

        var response = McpResponse<object>.Success(
            capabilities,
            new McpMeta { PaperlessBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }
}
