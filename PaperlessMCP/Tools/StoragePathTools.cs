using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using PaperlessMCP.Client;
using PaperlessMCP.Models.Common;
using PaperlessMCP.Models.StoragePaths;
using static PaperlessMCP.Utils.ParsingHelpers;

namespace PaperlessMCP.Tools;

/// <summary>
/// MCP tools for storage path operations.
/// </summary>
[McpServerToolType]
public static class StoragePathTools
{
    [McpServerTool(Name = "paperless-storage_paths-list")]
    [Description("List all storage paths with pagination.")]
    public static async Task<string> List(
        PaperlessClient client,
        [Description("Page number (default: 1)")] int page = 1,
        [Description("Page size (default: 25, max: 100)")] int pageSize = 25,
        [Description("Ordering field (e.g., 'name', '-document_count')")] string? ordering = null)
    {
        var result = await client.GetStoragePathsAsync(page, Math.Min(pageSize, 100), ordering).ConfigureAwait(false);

        var response = McpResponse<object>.Success(
            result.Results,
            new McpMeta
            {
                Page = page,
                PageSize = pageSize,
                Total = result.Count,
                Next = result.Next,
                PaperlessBaseUrl = client.BaseUrl
            }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "paperless-storage_paths-get")]
    [Description("Get a storage path by its ID.")]
    public static async Task<string> Get(
        PaperlessClient client,
        [Description("Storage path ID")] int id)
    {
        var storagePath = await client.GetStoragePathAsync(id).ConfigureAwait(false);

        if (storagePath == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.NotFound,
                $"Storage path with ID {id} not found",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<StoragePath>.Success(
            storagePath,
            new McpMeta { PaperlessBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "paperless-storage_paths-create")]
    [Description("Create a new storage path.")]
    public static async Task<string> Create(
        PaperlessClient client,
        [Description("Storage path name")] string name,
        [Description("Path template (e.g., '{correspondent}/{document_type}')")] string path,
        [Description("Match pattern for auto-assignment")] string? match = null,
        [Description("Matching algorithm (0=None, 1=Any, 2=All, 3=Literal, 4=Regex, 5=Fuzzy, 6=Auto)")] int? matchingAlgorithm = null)
    {
        var request = new StoragePathCreateRequest
        {
            Name = name,
            Path = path,
            Match = match,
            MatchingAlgorithm = matchingAlgorithm
        };

        var storagePath = await client.CreateStoragePathAsync(request).ConfigureAwait(false);

        if (storagePath == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                "Failed to create storage path",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<StoragePath>.Success(
            storagePath,
            new McpMeta { PaperlessBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "paperless-storage_paths-update")]
    [Description("Update an existing storage path.")]
    public static async Task<string> Update(
        PaperlessClient client,
        [Description("Storage path ID")] int id,
        [Description("New name (optional)")] string? name = null,
        [Description("Path template (optional)")] string? path = null,
        [Description("Match pattern (optional)")] string? match = null,
        [Description("Matching algorithm (optional)")] int? matchingAlgorithm = null)
    {
        var request = new StoragePathUpdateRequest
        {
            Name = name,
            Path = path,
            Match = match,
            MatchingAlgorithm = matchingAlgorithm
        };

        var storagePath = await client.UpdateStoragePathAsync(id, request).ConfigureAwait(false);

        if (storagePath == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.NotFound,
                $"Storage path with ID {id} not found or update failed",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<StoragePath>.Success(
            storagePath,
            new McpMeta { PaperlessBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "paperless-storage_paths-delete")]
    [Description("Delete a storage path. Requires explicit confirmation.")]
    public static async Task<string> Delete(
        PaperlessClient client,
        [Description("Storage path ID")] int id,
        [Description("Must be true to confirm deletion")] bool confirm = false)
    {
        if (!confirm)
        {
            var storagePath = await client.GetStoragePathAsync(id).ConfigureAwait(false);

            if (storagePath == null)
            {
                var notFoundResponse = McpErrorResponse.Create(
                    ErrorCodes.NotFound,
                    $"Storage path with ID {id} not found",
                    meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
                );
                return JsonSerializer.Serialize(notFoundResponse);
            }

            var dryRunResponse = McpErrorResponse.Create(
                ErrorCodes.ConfirmationRequired,
                "Deletion requires confirm=true. This is a dry run showing what would be deleted.",
                new { storage_path_id = id, name = storagePath.Name, path = storagePath.Path, document_count = storagePath.DocumentCount },
                new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(dryRunResponse);
        }

        var success = await client.DeleteStoragePathAsync(id).ConfigureAwait(false);

        if (!success)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                $"Failed to delete storage path with ID {id}",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<object>.Success(
            new { deleted = true, storage_path_id = id },
            new McpMeta { PaperlessBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "paperless-storage_paths-bulk_delete")]
    [Description("Delete multiple storage paths. Supports dry run mode.")]
    public static async Task<string> BulkDelete(
        PaperlessClient client,
        [Description("Storage path IDs (comma-separated)")] string storagePathIds,
        [Description("Dry run mode - shows what would be deleted without applying")] bool dryRun = true,
        [Description("Must be true to execute the deletion")] bool confirm = false)
    {
        var ids = ParseIntArray(storagePathIds);

        if (ids == null || ids.Length == 0)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.Validation,
                "No valid storage path IDs provided",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        if (dryRun || !confirm)
        {
            var dryRunResult = new BulkOperationResult
            {
                AffectedIds = ids,
                Warnings = new List<string>
                {
                    dryRun ? "This is a dry run. Set dry_run=false and confirm=true to execute." : "Set confirm=true to execute the operation."
                },
                Executed = false
            };

            var dryRunResponse = McpResponse<BulkOperationResult>.Success(
                dryRunResult,
                new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(dryRunResponse);
        }

        var success = await client.BulkEditObjectsAsync(ids, "storage_paths", "delete").ConfigureAwait(false);

        if (!success)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                "Bulk delete operation failed",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var result = new BulkOperationResult
        {
            AffectedIds = ids,
            Executed = true
        };

        var response = McpResponse<BulkOperationResult>.Success(
            result,
            new McpMeta { PaperlessBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

}
