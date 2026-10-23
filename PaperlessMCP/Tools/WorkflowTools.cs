using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using PaperlessMCP.Client;
using PaperlessMCP.Models.Common;
using PaperlessMCP.Models.Workflows;

namespace PaperlessMCP.Tools;

/// <summary>
/// MCP tools for workflow operations.
/// </summary>
[McpServerToolType]
public static class WorkflowTools
{
    private const string TriggersDescription =
        "Triggers as a JSON array, e.g. '[{\"type\": 1, \"sources\": [1, 2, 3], \"filter_path\": \"*/jg-dev/*\"}]'. " +
        "Trigger types: 1=Consumption Started, 2=Document Added, 3=Document Updated, 4=Scheduled. " +
        "Other fields: filter_filename, filter_mailrule, match, matching_algorithm, filter_has_tags, " +
        "filter_has_correspondent, filter_has_document_type. Include an existing trigger's \"id\" to modify it.";

    private const string ActionsDescription =
        "Actions as a JSON array, e.g. '[{\"type\": 1, \"assign_storage_path\": 5, \"assign_tags\": [3]}]'. " +
        "Action types: 1=Assignment, 2=Removal, 3=Email, 4=Webhook. " +
        "Assignment fields: assign_title, assign_tags, assign_correspondent, assign_document_type, " +
        "assign_storage_path, assign_owner, assign_custom_fields. Include an existing action's \"id\" to modify it.";

    [McpServerTool(Name = "paperless_workflows_list")]
    [Description("List all workflows with their triggers and actions.")]
    public static async Task<string> List(
        PaperlessClient client,
        [Description("Page number (default: 1)")] int page = 1,
        [Description("Page size (default: 25, capped by MAX_PAGE_SIZE)")] int pageSize = 25,
        [Description("Ordering field (e.g., 'order', 'name')")] string? ordering = null)
    {
        var effectivePageSize = client.GetEffectivePageSize(pageSize);
        var result = await client.GetWorkflowsAsync(page, effectivePageSize, ordering).ConfigureAwait(false);

        var response = McpResponse<object>.Success(
            result.Results,
            new McpMeta
            {
                Page = page,
                PageSize = effectivePageSize,
                Total = result.Count,
                Next = result.Next,
                PaperlessBaseUrl = client.BaseUrl
            }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "paperless_workflows_get")]
    [Description("Get a workflow by its ID, including its triggers and actions.")]
    public static async Task<string> Get(
        PaperlessClient client,
        [Description("Workflow ID")] int id)
    {
        var workflow = await client.GetWorkflowAsync(id).ConfigureAwait(false);

        if (workflow == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.NotFound,
                $"Workflow with ID {id} not found",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<Workflow>.Success(
            workflow,
            new McpMeta { PaperlessBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "paperless_workflows_create")]
    [Description("Create a new workflow with triggers and actions.")]
    public static async Task<string> Create(
        PaperlessClient client,
        [Description("Workflow name")] string name,
        [Description(TriggersDescription)] string triggers,
        [Description(ActionsDescription)] string actions,
        [Description("Execution order (optional)")] int? order = null,
        [Description("Whether the workflow is enabled (optional, default: true)")] bool? enabled = null)
    {
        var parsedTriggers = ParseJsonArray(triggers, nameof(triggers), out var triggersError);
        if (triggersError != null)
            return ValidationError(client, triggersError);

        var parsedActions = ParseJsonArray(actions, nameof(actions), out var actionsError);
        if (actionsError != null)
            return ValidationError(client, actionsError);

        var request = new WorkflowCreateRequest
        {
            Name = name,
            Order = order,
            Enabled = enabled,
            Triggers = parsedTriggers!.Value,
            Actions = parsedActions!.Value
        };

        var result = await client.CreateWorkflowWithResultAsync(request).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            var error = result.Error!;
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                $"Failed to create workflow: {error.Message}",
                new { status_code = (int)error.StatusCode, response_body = error.ResponseBody },
                new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<Workflow>.Success(
            result.Value!,
            new McpMeta { PaperlessBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "paperless_workflows_update")]
    [Description("Update an existing workflow. Passing triggers or actions replaces the whole list, so include " +
                 "every trigger/action that should remain (with its \"id\" to keep it). Use paperless_workflows_get first.")]
    public static async Task<string> Update(
        PaperlessClient client,
        [Description("Workflow ID")] int id,
        [Description("New name (optional)")] string? name = null,
        [Description("Execution order (optional)")] int? order = null,
        [Description("Enable or disable the workflow (optional)")] bool? enabled = null,
        [Description(TriggersDescription + " Optional.")] string? triggers = null,
        [Description(ActionsDescription + " Optional.")] string? actions = null)
    {
        JsonElement? parsedTriggers = null;
        if (triggers != null)
        {
            parsedTriggers = ParseJsonArray(triggers, nameof(triggers), out var triggersError);
            if (triggersError != null)
                return ValidationError(client, triggersError);
        }

        JsonElement? parsedActions = null;
        if (actions != null)
        {
            parsedActions = ParseJsonArray(actions, nameof(actions), out var actionsError);
            if (actionsError != null)
                return ValidationError(client, actionsError);
        }

        var request = new WorkflowUpdateRequest
        {
            Name = name,
            Order = order,
            Enabled = enabled,
            Triggers = parsedTriggers,
            Actions = parsedActions
        };

        var result = await client.UpdateWorkflowWithResultAsync(id, request).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            var error = result.Error!;
            var errorResponse = McpErrorResponse.Create(
                error.StatusCode == System.Net.HttpStatusCode.NotFound ? ErrorCodes.NotFound : ErrorCodes.UpstreamError,
                $"Failed to update workflow {id}: {error.Message}",
                new { status_code = (int)error.StatusCode, response_body = error.ResponseBody },
                new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<Workflow>.Success(
            result.Value!,
            new McpMeta { PaperlessBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "paperless_workflows_delete")]
    [Description("Delete a workflow. Requires explicit confirmation.")]
    public static async Task<string> Delete(
        PaperlessClient client,
        [Description("Workflow ID")] int id,
        [Description("Must be true to confirm deletion")] bool confirm = false)
    {
        if (!confirm)
        {
            var workflow = await client.GetWorkflowAsync(id).ConfigureAwait(false);

            if (workflow == null)
            {
                var notFoundResponse = McpErrorResponse.Create(
                    ErrorCodes.NotFound,
                    $"Workflow with ID {id} not found",
                    meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
                );
                return JsonSerializer.Serialize(notFoundResponse);
            }

            var dryRunResponse = McpErrorResponse.Create(
                ErrorCodes.ConfirmationRequired,
                "Deletion requires confirm=true. This is a dry run showing what would be deleted.",
                new { workflow_id = id, name = workflow.Name, enabled = workflow.Enabled },
                new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(dryRunResponse);
        }

        var success = await client.DeleteWorkflowAsync(id).ConfigureAwait(false);

        if (!success)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                $"Failed to delete workflow with ID {id}",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<object>.Success(
            new { deleted = true, workflow_id = id },
            new McpMeta { PaperlessBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    private static JsonElement? ParseJsonArray(string json, string parameterName, out string? error)
    {
        error = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                error = $"'{parameterName}' must be a JSON array";
                return null;
            }
            return document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            error = $"'{parameterName}' is not valid JSON: {ex.Message}";
            return null;
        }
    }

    private static string ValidationError(PaperlessClient client, string message)
    {
        var errorResponse = McpErrorResponse.Create(
            ErrorCodes.Validation,
            message,
            meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(errorResponse);
    }
}
