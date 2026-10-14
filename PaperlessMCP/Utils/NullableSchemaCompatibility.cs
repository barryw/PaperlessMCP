using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace PaperlessMCP.Utils;

/// <summary>
/// Collapses nullable tool parameters (<c>"type": ["integer", "null"], "default": null</c>) to a plain type.
/// Some MCP clients cannot represent the union type and substitute 0 or false as the default, which turns an
/// omitted filter into a real one (e.g. <c>correspondent__id=0</c>). Omitted arguments still bind to null.
/// </summary>
internal static class NullableSchemaCompatibility
{
    public static ListToolsResult Apply(ListToolsResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        foreach (var tool in result.Tools)
            tool.InputSchema = Simplify(tool.InputSchema);

        return result;
    }

    public static JsonElement Simplify(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object ||
            JsonNode.Parse(schema.GetRawText()) is not JsonObject root ||
            root["properties"] is not JsonObject properties)
        {
            return schema;
        }

        var changed = false;
        foreach (var (_, value) in properties)
        {
            if (value is not JsonObject property || property["type"] is not JsonArray types)
                continue;

            var nonNullTypes = types
                .Select(t => t?.GetValue<string>())
                .Where(t => t != "null")
                .ToList();

            if (nonNullTypes.Count != 1 || nonNullTypes.Count == types.Count)
                continue;

            property["type"] = nonNullTypes[0];
            if (property.TryGetPropertyValue("default", out var defaultValue) && defaultValue is null)
                property.Remove("default");

            changed = true;
        }

        return changed ? JsonSerializer.SerializeToElement(root) : schema;
    }
}
