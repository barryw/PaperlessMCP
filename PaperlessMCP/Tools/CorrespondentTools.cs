using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using PaperlessMCP.Client;
using PaperlessMCP.Models.Common;
using PaperlessMCP.Models.Correspondents;
using static PaperlessMCP.Utils.ParsingHelpers;

namespace PaperlessMCP.Tools;

/// <summary>
/// MCP tools for correspondent operations.
/// </summary>
[McpServerToolType]
public static class CorrespondentTools
{
    [McpServerTool(Name = "paperless-correspondents-list")]
    [Description("List all correspondents with pagination.")]
    public static async Task<string> List(
        PaperlessClient client,
        [Description("Page number (default: 1)")] int page = 1,
        [Description("Page size (default: 25, max: 100)")] int pageSize = 25,
        [Description("Ordering field (e.g., 'name', '-document_count', 'last_correspondence')")] string? ordering = null)
    {
        var result = await client.GetCorrespondentsAsync(page, Math.Min(pageSize, 100), ordering).ConfigureAwait(false);

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

    [McpServerTool(Name = "paperless-correspondents-get")]
    [Description("Get a correspondent by its ID.")]
    public static async Task<string> Get(
        PaperlessClient client,
        [Description("Correspondent ID")] int id)
    {
        var correspondent = await client.GetCorrespondentAsync(id).ConfigureAwait(false);

        if (correspondent == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.NotFound,
                $"Correspondent with ID {id} not found",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<Correspondent>.Success(
            correspondent,
            new McpMeta { PaperlessBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "paperless-correspondents-create")]
    [Description("Create a new correspondent.")]
    public static async Task<string> Create(
        PaperlessClient client,
        [Description("Correspondent name")] string name,
        [Description("Match pattern for auto-assignment")] string? match = null,
        [Description("Matching algorithm (0=None, 1=Any, 2=All, 3=Literal, 4=Regex, 5=Fuzzy, 6=Auto)")] int? matchingAlgorithm = null)
    {
        var request = new CorrespondentCreateRequest
        {
            Name = name,
            Match = match,
            MatchingAlgorithm = matchingAlgorithm
        };

        var correspondent = await client.CreateCorrespondentAsync(request).ConfigureAwait(false);

        if (correspondent == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                "Failed to create correspondent",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<Correspondent>.Success(
            correspondent,
            new McpMeta { PaperlessBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "paperless-correspondents-update")]
    [Description("Update an existing correspondent.")]
    public static async Task<string> Update(
        PaperlessClient client,
        [Description("Correspondent ID")] int id,
        [Description("New name (optional)")] string? name = null,
        [Description("Match pattern (optional)")] string? match = null,
        [Description("Matching algorithm (optional)")] int? matchingAlgorithm = null)
    {
        var request = new CorrespondentUpdateRequest
        {
            Name = name,
            Match = match,
            MatchingAlgorithm = matchingAlgorithm
        };

        var correspondent = await client.UpdateCorrespondentAsync(id, request).ConfigureAwait(false);

        if (correspondent == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.NotFound,
                $"Correspondent with ID {id} not found or update failed",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<Correspondent>.Success(
            correspondent,
            new McpMeta { PaperlessBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "paperless-correspondents-delete")]
    [Description("Delete a correspondent. Requires explicit confirmation.")]
    public static async Task<string> Delete(
        PaperlessClient client,
        [Description("Correspondent ID")] int id,
        [Description("Must be true to confirm deletion")] bool confirm = false)
    {
        if (!confirm)
        {
            var correspondent = await client.GetCorrespondentAsync(id).ConfigureAwait(false);

            if (correspondent == null)
            {
                var notFoundResponse = McpErrorResponse.Create(
                    ErrorCodes.NotFound,
                    $"Correspondent with ID {id} not found",
                    meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
                );
                return JsonSerializer.Serialize(notFoundResponse);
            }

            var dryRunResponse = McpErrorResponse.Create(
                ErrorCodes.ConfirmationRequired,
                "Deletion requires confirm=true. This is a dry run showing what would be deleted.",
                new { correspondent_id = id, name = correspondent.Name, document_count = correspondent.DocumentCount },
                new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(dryRunResponse);
        }

        var success = await client.DeleteCorrespondentAsync(id).ConfigureAwait(false);

        if (!success)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                $"Failed to delete correspondent with ID {id}",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<object>.Success(
            new { deleted = true, correspondent_id = id },
            new McpMeta { PaperlessBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "paperless-correspondents-bulk_delete")]
    [Description("Delete multiple correspondents. Supports dry run mode.")]
    public static async Task<string> BulkDelete(
        PaperlessClient client,
        [Description("Correspondent IDs (comma-separated)")] string correspondentIds,
        [Description("Dry run mode - shows what would be deleted without applying")] bool dryRun = true,
        [Description("Must be true to execute the deletion")] bool confirm = false)
    {
        var ids = ParseIntArray(correspondentIds);

        if (ids == null || ids.Length == 0)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.Validation,
                "No valid correspondent IDs provided",
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

        var success = await client.BulkEditObjectsAsync(ids, "correspondents", "delete").ConfigureAwait(false);

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
