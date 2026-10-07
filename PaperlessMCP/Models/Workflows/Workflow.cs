using System.Text.Json;
using System.Text.Json.Serialization;

namespace PaperlessMCP.Models.Workflows;

/// <summary>
/// Represents a workflow in Paperless-ngx.
/// Triggers and actions are passed through as raw JSON because their schema
/// is large and differs between Paperless-ngx versions.
/// </summary>
public record Workflow
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("order")]
    public int Order { get; init; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("triggers")]
    public List<JsonElement> Triggers { get; init; } = new();

    [JsonPropertyName("actions")]
    public List<JsonElement> Actions { get; init; } = new();
}

/// <summary>
/// Request to create a new workflow.
/// </summary>
public record WorkflowCreateRequest
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("order")]
    public int? Order { get; init; }

    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }

    [JsonPropertyName("triggers")]
    public required JsonElement Triggers { get; init; }

    [JsonPropertyName("actions")]
    public required JsonElement Actions { get; init; }
}

/// <summary>
/// Request to update an existing workflow.
/// </summary>
public record WorkflowUpdateRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("order")]
    public int? Order { get; init; }

    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }

    [JsonPropertyName("triggers")]
    public JsonElement? Triggers { get; init; }

    [JsonPropertyName("actions")]
    public JsonElement? Actions { get; init; }
}
