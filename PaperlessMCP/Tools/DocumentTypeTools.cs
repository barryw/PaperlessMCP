using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using PaperlessMCP.Client;
using PaperlessMCP.Models.Common;
using PaperlessMCP.Models.DocumentTypes;
using static PaperlessMCP.Utils.ParsingHelpers;

namespace PaperlessMCP.Tools;

/// <summary>
/// MCP tools for document type operations.
/// </summary>
[McpServerToolType]
public static class DocumentTypeTools
{
    [McpServerTool(Name = "paperless-document_types-list")]
    [Description("List all document types with pagination.")]
    public static async Task<string> List(
        PaperlessClient client,
        [Description("Page number (default: 1)")] int page = 1,
        [Description("Page size (default: 25, max: 100)")] int pageSize = 25,
        [Description("Ordering field (e.g., 'name', '-document_count')")] string? ordering = null)
    {
        var result = await client.GetDocumentTypesAsync(page, Math.Min(pageSize, 100), ordering).ConfigureAwait(false);

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

    [McpServerTool(Name = "paperless-document_types-get")]
    [Description("Get a document type by its ID.")]
    public static async Task<string> Get(
        PaperlessClient client,
        [Description("Document type ID")] int id)
    {
        var documentType = await client.GetDocumentTypeAsync(id).ConfigureAwait(false);

        if (documentType == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.NotFound,
                $"Document type with ID {id} not found",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<DocumentType>.Success(
            documentType,
            new McpMeta { PaperlessBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "paperless-document_types-create")]
    [Description("Create a new document type.")]
    public static async Task<string> Create(
        PaperlessClient client,
        [Description("Document type name")] string name,
        [Description("Match pattern for auto-assignment")] string? match = null,
        [Description("Matching algorithm (0=None, 1=Any, 2=All, 3=Literal, 4=Regex, 5=Fuzzy, 6=Auto)")] int? matchingAlgorithm = null)
    {
        var request = new DocumentTypeCreateRequest
        {
            Name = name,
            Match = match,
            MatchingAlgorithm = matchingAlgorithm
        };

        var documentType = await client.CreateDocumentTypeAsync(request).ConfigureAwait(false);

        if (documentType == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                "Failed to create document type",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<DocumentType>.Success(
            documentType,
            new McpMeta { PaperlessBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "paperless-document_types-update")]
    [Description("Update an existing document type.")]
    public static async Task<string> Update(
        PaperlessClient client,
        [Description("Document type ID")] int id,
        [Description("New name (optional)")] string? name = null,
        [Description("Match pattern (optional)")] string? match = null,
        [Description("Matching algorithm (optional)")] int? matchingAlgorithm = null)
    {
        var request = new DocumentTypeUpdateRequest
        {
            Name = name,
            Match = match,
            MatchingAlgorithm = matchingAlgorithm
        };

        var documentType = await client.UpdateDocumentTypeAsync(id, request).ConfigureAwait(false);

        if (documentType == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.NotFound,
                $"Document type with ID {id} not found or update failed",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<DocumentType>.Success(
            documentType,
            new McpMeta { PaperlessBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "paperless-document_types-delete")]
    [Description("Delete a document type. Requires explicit confirmation.")]
    public static async Task<string> Delete(
        PaperlessClient client,
        [Description("Document type ID")] int id,
        [Description("Must be true to confirm deletion")] bool confirm = false)
    {
        if (!confirm)
        {
            var documentType = await client.GetDocumentTypeAsync(id).ConfigureAwait(false);

            if (documentType == null)
            {
                var notFoundResponse = McpErrorResponse.Create(
                    ErrorCodes.NotFound,
                    $"Document type with ID {id} not found",
                    meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
                );
                return JsonSerializer.Serialize(notFoundResponse);
            }

            var dryRunResponse = McpErrorResponse.Create(
                ErrorCodes.ConfirmationRequired,
                "Deletion requires confirm=true. This is a dry run showing what would be deleted.",
                new { document_type_id = id, name = documentType.Name, document_count = documentType.DocumentCount },
                new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(dryRunResponse);
        }

        var success = await client.DeleteDocumentTypeAsync(id).ConfigureAwait(false);

        if (!success)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                $"Failed to delete document type with ID {id}",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<object>.Success(
            new { deleted = true, document_type_id = id },
            new McpMeta { PaperlessBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "paperless-document_types-bulk_delete")]
    [Description("Delete multiple document types. Supports dry run mode.")]
    public static async Task<string> BulkDelete(
        PaperlessClient client,
        [Description("Document type IDs (comma-separated)")] string documentTypeIds,
        [Description("Dry run mode - shows what would be deleted without applying")] bool dryRun = true,
        [Description("Must be true to execute the deletion")] bool confirm = false)
    {
        var ids = ParseIntArray(documentTypeIds);

        if (ids == null || ids.Length == 0)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.Validation,
                "No valid document type IDs provided",
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

        var success = await client.BulkEditObjectsAsync(ids, "document_types", "delete").ConfigureAwait(false);

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
