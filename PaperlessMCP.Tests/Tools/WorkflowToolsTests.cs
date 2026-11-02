using System.Net;
using System.Text.Json;
using FluentAssertions;
using PaperlessMCP.Tests.Fixtures;
using RichardSzalay.MockHttp;
using PaperlessMCP.Tools;
using Xunit;

namespace PaperlessMCP.Tests.Tools;

public class WorkflowToolsTests : IDisposable
{
    private readonly MockHttpClientFactory _factory;

    public WorkflowToolsTests()
    {
        _factory = new MockHttpClientFactory();
    }

    public void Dispose()
    {
        _factory.Dispose();
    }

    private static string CreateWorkflowJson(int id = 1, string name = "Test Workflow") => $$"""
        {
          "id": {{id}},
          "name": "{{name}}",
          "order": 0,
          "enabled": true,
          "triggers": [{ "id": 10, "type": 1, "sources": [1, 2, 3], "filter_path": "*/jg-dev/*" }],
          "actions": [{ "id": 20, "type": 1, "assign_storage_path": 45, "assign_tags": [3] }]
        }
        """;

    [Fact]
    public async Task List_ReturnsWorkflowsWithTriggersAndActions()
    {
        // Arrange
        _factory.MockHandler
            .When(HttpMethod.Get, "https://paperless.example.com/api/workflows/*")
            .Respond("application/json", $$"""{ "count": 2, "next": null, "previous": null, "results": [{{CreateWorkflowJson(1, "A")}}, {{CreateWorkflowJson(2, "B")}}] }""");

        // Act
        var result = await WorkflowTools.List(_factory.Client);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        var workflows = json.RootElement.GetProperty("result");
        workflows.GetArrayLength().Should().Be(2);
        workflows[0].GetProperty("triggers")[0].GetProperty("filter_path").GetString().Should().Be("*/jg-dev/*");
        workflows[0].GetProperty("actions")[0].GetProperty("assign_storage_path").GetInt32().Should().Be(45);
    }

    [Fact]
    public async Task Get_WhenExists_ReturnsWorkflow()
    {
        // Arrange
        _factory.SetupGet("api/workflows/1/", CreateWorkflowJson(1, "Amazon"));

        // Act
        var result = await WorkflowTools.Get(_factory.Client, 1);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result").GetProperty("name").GetString().Should().Be("Amazon");
    }

    [Fact]
    public async Task Get_WhenNotFound_ReturnsError()
    {
        // Arrange
        _factory.SetupGetWithStatus("api/workflows/999/", HttpStatusCode.NotFound);

        // Act
        var result = await WorkflowTools.Get(_factory.Client, 999);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Create_SendsTriggersAndActionsAsJson()
    {
        // Arrange
        string? body = null;
        _factory.MockHandler
            .When(HttpMethod.Post, "https://paperless.example.com/api/workflows/")
            .With(request =>
            {
                body = request.Content!.ReadAsStringAsync().Result;
                return true;
            })
            .Respond("application/json", CreateWorkflowJson(1, "New"));

        // Act
        var result = await WorkflowTools.Create(
            _factory.Client,
            "New",
            """[{"type": 1, "sources": [1], "filter_path": "*/jg-dev/*"}]""",
            """[{"type": 1, "assign_storage_path": 45}]""");

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        var sent = JsonDocument.Parse(body!).RootElement;
        sent.GetProperty("name").GetString().Should().Be("New");
        sent.GetProperty("triggers")[0].GetProperty("filter_path").GetString().Should().Be("*/jg-dev/*");
        sent.GetProperty("actions")[0].GetProperty("assign_storage_path").GetInt32().Should().Be(45);
        sent.TryGetProperty("order", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Create_WithInvalidJson_ReturnsValidationError()
    {
        // Act
        var result = await WorkflowTools.Create(_factory.Client, "New", "not json", "[]");

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION");
    }

    [Fact]
    public async Task Create_WithNonArrayJson_ReturnsValidationError()
    {
        // Act
        var result = await WorkflowTools.Create(_factory.Client, "New", "[]", """{"type": 1}""");

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("message").GetString().Should().Contain("actions");
    }

    [Fact]
    public async Task Create_WhenUpstreamRejects_ReturnsResponseBody()
    {
        // Arrange
        _factory.SetupPostWithError("api/workflows/", HttpStatusCode.BadRequest, """{"triggers": ["invalid"]}""");

        // Act
        var result = await WorkflowTools.Create(_factory.Client, "New", "[]", "[]");

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("UPSTREAM_ERROR");
        json.RootElement.GetProperty("error").GetProperty("details").GetProperty("response_body").GetString().Should().Contain("invalid");
    }

    [Fact]
    public async Task Update_OnlySendsProvidedFields()
    {
        // Arrange
        string? body = null;
        _factory.MockHandler
            .When(HttpMethod.Patch, "https://paperless.example.com/api/workflows/1/")
            .With(request =>
            {
                body = request.Content!.ReadAsStringAsync().Result;
                return true;
            })
            .Respond("application/json", CreateWorkflowJson(1, "Test Workflow"));

        // Act
        var result = await WorkflowTools.Update(_factory.Client, 1, enabled: false);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        var sent = JsonDocument.Parse(body!).RootElement;
        sent.GetProperty("enabled").GetBoolean().Should().BeFalse();
        sent.TryGetProperty("triggers", out _).Should().BeFalse();
        sent.TryGetProperty("actions", out _).Should().BeFalse();
        sent.TryGetProperty("name", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Update_WhenNotFound_ReturnsNotFound()
    {
        // Arrange
        _factory.SetupPatchWithStatus("api/workflows/999/", HttpStatusCode.NotFound);

        // Act
        var result = await WorkflowTools.Update(_factory.Client, 999, name: "X");

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Delete_WithoutConfirmation_ReturnsDryRun()
    {
        // Arrange
        _factory.SetupGet("api/workflows/1/", CreateWorkflowJson(1, "To Delete"));

        // Act
        var result = await WorkflowTools.Delete(_factory.Client, 1, confirm: false);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("CONFIRMATION_REQUIRED");
    }

    [Fact]
    public async Task Delete_WithConfirmation_DeletesWorkflow()
    {
        // Arrange
        _factory.SetupDelete("api/workflows/1/", HttpStatusCode.NoContent);

        // Act
        var result = await WorkflowTools.Delete(_factory.Client, 1, confirm: true);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result").GetProperty("deleted").GetBoolean().Should().BeTrue();
    }
}
