using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using PaperlessMCP.Client;
using PaperlessMCP.Models.Common;
using PaperlessMCP.Models.CustomFields;
using PaperlessMCP.Models.Documents;

namespace PaperlessMCP.Tools;

/// <summary>
/// MCP tools for custom field operations.
/// </summary>
[McpServerToolType]
public static class CustomFieldTools
{
    [McpServerTool(Name = "paperless-custom_fields-list")]
    [Description("List all custom field definitions with pagination.")]
    public static async Task<string> List(
        PaperlessClient client,
        [Description("Page number (default: 1)")] int page = 1,
        [Description("Page size (default: 25, max: 100)")] int pageSize = 25)
    {
        var result = await client.GetCustomFieldsAsync(page, Math.Min(pageSize, 100)).ConfigureAwait(false);

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

    [McpServerTool(Name = "paperless-custom_fields-get")]
    [Description("Get a custom field definition by its ID.")]
    public static async Task<string> Get(
        PaperlessClient client,
        [Description("Custom field ID")] int id)
    {
        var customField = await client.GetCustomFieldAsync(id).ConfigureAwait(false);

        if (customField == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.NotFound,
                $"Custom field with ID {id} not found",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<CustomField>.Success(
            customField,
            new McpMeta { PaperlessBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "paperless-custom_fields-create")]
    [Description("Create a new custom field definition.")]
    public static async Task<string> Create(
        PaperlessClient client,
        [Description("Custom field name")] string name,
        [Description("Data type: string, url, date, boolean, integer, float, monetary, documentlink, select")] string dataType,
        [Description("Select options (comma-separated, for 'select' type only)")] string? selectOptions = null,
        [Description("Default currency (for 'monetary' type only)")] string? defaultCurrency = null)
    {
        CustomFieldExtraData? extraData = null;

        if (dataType == CustomFieldDataType.Select && !string.IsNullOrEmpty(selectOptions))
        {
            extraData = new CustomFieldExtraData
            {
                SelectOptions = selectOptions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
            };
        }
        else if (dataType == CustomFieldDataType.Monetary && !string.IsNullOrEmpty(defaultCurrency))
        {
            extraData = new CustomFieldExtraData
            {
                DefaultCurrency = defaultCurrency
            };
        }

        var request = new CustomFieldCreateRequest
        {
            Name = name,
            DataType = dataType,
            ExtraData = extraData
        };

        var customField = await client.CreateCustomFieldAsync(request).ConfigureAwait(false);

        if (customField == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                "Failed to create custom field",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<CustomField>.Success(
            customField,
            new McpMeta { PaperlessBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "paperless-custom_fields-update")]
    [Description("Update an existing custom field definition.")]
    public static async Task<string> Update(
        PaperlessClient client,
        [Description("Custom field ID")] int id,
        [Description("New name (optional)")] string? name = null,
        [Description("Select options (comma-separated, for 'select' type only, optional)")] string? selectOptions = null,
        [Description("Default currency (for 'monetary' type only, optional)")] string? defaultCurrency = null)
    {
        CustomFieldExtraData? extraData = null;

        if (!string.IsNullOrEmpty(selectOptions))
        {
            extraData = new CustomFieldExtraData
            {
                SelectOptions = selectOptions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
            };
        }
        else if (!string.IsNullOrEmpty(defaultCurrency))
        {
            extraData = new CustomFieldExtraData
            {
                DefaultCurrency = defaultCurrency
            };
        }

        var request = new CustomFieldUpdateRequest
        {
            Name = name,
            ExtraData = extraData
        };

        var customField = await client.UpdateCustomFieldAsync(id, request).ConfigureAwait(false);

        if (customField == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.NotFound,
                $"Custom field with ID {id} not found or update failed",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<CustomField>.Success(
            customField,
            new McpMeta { PaperlessBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "paperless-custom_fields-delete")]
    [Description("Delete a custom field definition. Requires explicit confirmation.")]
    public static async Task<string> Delete(
        PaperlessClient client,
        [Description("Custom field ID")] int id,
        [Description("Must be true to confirm deletion")] bool confirm = false)
    {
        if (!confirm)
        {
            var customField = await client.GetCustomFieldAsync(id).ConfigureAwait(false);

            if (customField == null)
            {
                var notFoundResponse = McpErrorResponse.Create(
                    ErrorCodes.NotFound,
                    $"Custom field with ID {id} not found",
                    meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
                );
                return JsonSerializer.Serialize(notFoundResponse);
            }

            var dryRunResponse = McpErrorResponse.Create(
                ErrorCodes.ConfirmationRequired,
                "Deletion requires confirm=true. This is a dry run showing what would be deleted.",
                new { custom_field_id = id, name = customField.Name, data_type = customField.DataType },
                new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(dryRunResponse);
        }

        var success = await client.DeleteCustomFieldAsync(id).ConfigureAwait(false);

        if (!success)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                $"Failed to delete custom field with ID {id}",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<object>.Success(
            new { deleted = true, custom_field_id = id },
            new McpMeta { PaperlessBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "paperless-custom_fields-assign")]
    [Description("Assign a custom field value to a document.")]
    public static async Task<string> Assign(
        PaperlessClient client,
        [Description("Document ID")] int documentId,
        [Description("Custom field ID")] int fieldId,
        [Description("Value to assign (string, number, boolean, or date depending on field type)")] string value)
    {
        // Get current document to update its custom fields
        var document = await client.GetDocumentAsync(documentId).ConfigureAwait(false);

        if (document == null)
        {
            var notFoundResponse = McpErrorResponse.Create(
                ErrorCodes.NotFound,
                $"Document with ID {documentId} not found",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(notFoundResponse);
        }

        // Get field definition to understand the data type
        var field = await client.GetCustomFieldAsync(fieldId).ConfigureAwait(false);

        if (field == null)
        {
            var notFoundResponse = McpErrorResponse.Create(
                ErrorCodes.NotFound,
                $"Custom field with ID {fieldId} not found",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(notFoundResponse);
        }

        // Parse value based on field type
        object? parsedValue = field.DataType switch
        {
            CustomFieldDataType.Boolean => bool.TryParse(value, out var b) ? b : null,
            CustomFieldDataType.Integer => int.TryParse(value, out var i) ? i : null,
            CustomFieldDataType.Float => double.TryParse(value, out var d) ? d : null,
            CustomFieldDataType.Date => value, // Keep as string for dates
            _ => value
        };

        // Update custom fields list
        var customFields = document.CustomFields.ToList();
        var existingIndex = customFields.FindIndex(cf => cf.Field == fieldId);

        if (existingIndex >= 0)
        {
            customFields[existingIndex] = new DocumentCustomField { Field = fieldId, Value = parsedValue };
        }
        else
        {
            customFields.Add(new DocumentCustomField { Field = fieldId, Value = parsedValue });
        }

        // Update document
        var updateRequest = new DocumentUpdateRequest
        {
            CustomFields = customFields
        };

        var updatedDocument = await client.UpdateDocumentAsync(documentId, updateRequest).ConfigureAwait(false);

        if (updatedDocument == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                "Failed to assign custom field to document",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<object>.Success(
            new
            {
                document_id = documentId,
                field_id = fieldId,
                field_name = field.Name,
                value = parsedValue,
                message = "Custom field assigned successfully"
            },
            new McpMeta { PaperlessBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }
}
