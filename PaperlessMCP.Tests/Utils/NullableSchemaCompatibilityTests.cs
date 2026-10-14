using System.Text.Json;
using FluentAssertions;
using PaperlessMCP.Utils;
using Xunit;

namespace PaperlessMCP.Tests.Utils;

public class NullableSchemaCompatibilityTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Simplify_WhenPropertyIsNullableInteger_CollapsesTypeAndDropsNullDefault()
    {
        var schema = Parse("""
            {"type":"object","properties":{"correspondent":{"description":"Filter","type":["integer","null"],"default":null}}}
            """);

        var result = NullableSchemaCompatibility.Simplify(schema);

        var property = result.GetProperty("properties").GetProperty("correspondent");
        property.GetProperty("type").GetString().Should().Be("integer");
        property.TryGetProperty("default", out _).Should().BeFalse();
        property.GetProperty("description").GetString().Should().Be("Filter");
    }

    [Fact]
    public void Simplify_WhenPropertyIsNullableBoolean_CollapsesType()
    {
        var schema = Parse("""
            {"type":"object","properties":{"isInboxTag":{"type":["boolean","null"],"default":null}}}
            """);

        var result = NullableSchemaCompatibility.Simplify(schema);

        result.GetProperty("properties").GetProperty("isInboxTag").GetProperty("type").GetString().Should().Be("boolean");
    }

    [Fact]
    public void Simplify_WhenDefaultIsNotNull_KeepsDefault()
    {
        var schema = Parse("""
            {"type":"object","properties":{"page":{"type":["integer","null"],"default":1}}}
            """);

        var result = NullableSchemaCompatibility.Simplify(schema);

        result.GetProperty("properties").GetProperty("page").GetProperty("default").GetInt32().Should().Be(1);
    }

    [Fact]
    public void Simplify_WhenNoNullableProperties_ReturnsSchemaUnchanged()
    {
        const string json = """{"type":"object","properties":{"id":{"type":"integer"}},"required":["id"]}""";

        var result = NullableSchemaCompatibility.Simplify(Parse(json));

        result.GetRawText().Should().Be(json);
    }
}
