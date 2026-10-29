using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using ModelContextProtocol.Server;
using Xunit;

namespace PaperlessMCP.Tests.Tools;

public class ToolNamingTests
{
    private static readonly Regex AnthropicToolNamePattern = new("^[a-zA-Z0-9_-]{1,64}$");

    [Fact]
    public void AllToolNames_ShouldMatchAnthropicApiNamingRules()
    {
        var toolAssembly = typeof(PaperlessMCP.Tools.HealthTools).Assembly;

        var toolNames = toolAssembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            .SelectMany(m => m.GetCustomAttributes<McpServerToolAttribute>())
            .Where(a => a.Name is not null)
            .Select(a => a.Name!)
            .ToList();

        toolNames.Should().NotBeEmpty("expected to find at least one McpServerTool attribute");

        var violations = toolNames
            .Where(name => !AnthropicToolNamePattern.IsMatch(name))
            .ToList();

        violations.Should().BeEmpty(
            "tool names must match ^[a-zA-Z0-9_-]{{1,64}}$ per Anthropic API rules, " +
            $"but found: {string.Join(", ", violations)}");
    }

    [Fact]
    public void AllToolNames_ShouldNotContainDots()
    {
        var toolAssembly = typeof(PaperlessMCP.Tools.HealthTools).Assembly;

        var toolNames = toolAssembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            .SelectMany(m => m.GetCustomAttributes<McpServerToolAttribute>())
            .Where(a => a.Name is not null)
            .Select(a => a.Name!)
            .ToList();

        var dotNames = toolNames.Where(name => name.Contains('.')).ToList();

        dotNames.Should().BeEmpty(
            $"dots are not allowed in tool names, but found: {string.Join(", ", dotNames)}");
    }

    [Fact]
    public void DocumentUpdateTagsSchema_AcceptsArraysAndStrings()
    {
        var method = typeof(PaperlessMCP.Tools.DocumentTools).GetMethod(nameof(PaperlessMCP.Tools.DocumentTools.Update))!;
        var schema = McpServerTool.Create(method, (object?)null).ProtocolTool.InputSchema;
        var tags = schema.GetProperty("properties").GetProperty("tags");

        tags.TryGetProperty("type", out _).Should().BeFalse();
        tags.GetProperty("description").GetString().Should().Contain("integer array or comma-separated string");
    }
}
