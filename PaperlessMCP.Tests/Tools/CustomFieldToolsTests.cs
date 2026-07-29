using System.Net;
using System.Text.Json;
using FluentAssertions;
using PaperlessMCP.Models.CustomFields;
using PaperlessMCP.Tests.Fixtures;
using RichardSzalay.MockHttp;
using PaperlessMCP.Tools;
using Xunit;

namespace PaperlessMCP.Tests.Tools;

public class CustomFieldToolsTests : IDisposable
{
    private readonly MockHttpClientFactory _factory;

    public CustomFieldToolsTests()
    {
        _factory = new MockHttpClientFactory();
    }

    public void Dispose()
    {
        _factory.Dispose();
    }

    [Fact]
    public async Task List_ReturnsCustomFieldList()
    {
        // Arrange
        _factory.MockHandler
            .When(HttpMethod.Get, "https://paperless.example.com/api/custom_fields/*")
            .Respond("application/json", TestFixtures.CustomFields.CreateCustomFieldListJson(4));

        // Act
        var result = await CustomFieldTools.List(_factory.Client);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result").GetArrayLength().Should().Be(4);
    }

    [Fact]
    public async Task Get_WhenExists_ReturnsCustomField()
    {
        // Arrange
        _factory.SetupGet("api/custom_fields/1/", TestFixtures.CustomFields.CreateCustomFieldJson(1, "Invoice Number"));

        // Act
        var result = await CustomFieldTools.Get(_factory.Client, 1);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result").GetProperty("name").GetString().Should().Be("Invoice Number");
    }

    [Fact]
    public async Task Get_WhenNotFound_ReturnsError()
    {
        // Arrange
        _factory.SetupGetWithStatus("api/custom_fields/999/", HttpStatusCode.NotFound);

        // Act
        var result = await CustomFieldTools.Get(_factory.Client, 999);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Create_StringField_ReturnsCreatedField()
    {
        // Arrange
        _factory.SetupPost("api/custom_fields/", TestFixtures.CustomFields.CreateCustomFieldJson(1, "Reference Number"));

        // Act
        var result = await CustomFieldTools.Create(_factory.Client, "Reference Number", "string");

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result").GetProperty("name").GetString().Should().Be("Reference Number");
    }

    [Fact]
    public async Task Create_SelectField_IncludesOptions()
    {
        // Arrange
        var selectField = TestFixtures.CustomFields.CreateCustomField(1, "Status", "select");
        _factory.SetupGet("api/status/", """{"pngx_version":"2.20.15"}""");
        _factory.MockHandler
            .When(HttpMethod.Post, "https://paperless.example.com/api/custom_fields/")
            .WithJsonContent<JsonElement>(json =>
                json.GetProperty("name").GetString() == "Status"
                && json.GetProperty("data_type").GetString() == "select"
                && HasObjectSelectOptions(
                    json,
                    new SelectOption { Label = "Pending" },
                    new SelectOption { Label = "Approved" },
                    new SelectOption { Label = "Rejected" }))
            .Respond("application/json", JsonSerializer.Serialize(selectField));

        // Act
        var result = await CustomFieldTools.Create(
            _factory.Client,
            "Status",
            "select",
            selectOptions: "Pending,Approved,Rejected");

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result")
            .GetProperty("extra_data")
            .GetProperty("select_options")[0]
            .GetProperty("id")
            .GetString()
            .Should().Be("option-1");
    }

    [Fact]
    public async Task Create_SelectField_BeforeV214_UsesStringOptions()
    {
        // Arrange
        string? requestBody = null;
        _factory.SetupGet("api/status/", """{"pngx_version":"2.13.5"}""");
        _factory.MockHandler
            .When(HttpMethod.Post, "https://paperless.example.com/api/custom_fields/")
            .With(request =>
            {
                requestBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return true;
            })
            .Respond(
                "application/json",
                """{"id":1,"name":"Status","data_type":"select","extra_data":{"select_options":["Pending","Approved","Rejected"]}}""");

        // Act
        var result = await CustomFieldTools.Create(
            _factory.Client,
            "Status",
            "select",
            selectOptions: "Pending,Approved,Rejected");

        // Assert
        using var requestJson = JsonDocument.Parse(requestBody!);
        HasStringSelectOptions(requestJson.RootElement, "Pending", "Approved", "Rejected").Should().BeTrue();
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue("because response was {0}", result);
        var firstOption = json.RootElement.GetProperty("result")
            .GetProperty("extra_data")
            .GetProperty("select_options")[0];
        firstOption.GetProperty("label").GetString().Should().Be("Pending");
        firstOption.TryGetProperty("id", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Update_WhenSuccessful_ReturnsUpdatedField()
    {
        // Arrange
        _factory.SetupPatch("api/custom_fields/1/", TestFixtures.CustomFields.CreateCustomFieldJson(1, "Updated Field"));

        // Act
        var result = await CustomFieldTools.Update(_factory.Client, 1, name: "Updated Field");

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result").GetProperty("name").GetString().Should().Be("Updated Field");
    }

    [Fact]
    public async Task Update_SelectField_PreservesExistingOptionIds()
    {
        // Arrange
        var existingField = new CustomField
        {
            Id = 1,
            Name = "Status",
            DataType = "select",
            ExtraData = new CustomFieldExtraData
            {
                SelectOptions =
                [
                    new SelectOption { Id = "pending-id", Label = "Pending" },
                    new SelectOption { Id = "approved-id", Label = "Approved" }
                ]
            }
        };
        var updatedField = existingField with
        {
            ExtraData = new CustomFieldExtraData
            {
                SelectOptions =
                [
                    new SelectOption { Id = "pending-id", Label = "Pending" },
                    new SelectOption { Id = "approved-id", Label = "Approved" },
                    new SelectOption { Id = "rejected-id", Label = "Rejected" }
                ]
            }
        };

        _factory.SetupGet("api/custom_fields/1/", JsonSerializer.Serialize(existingField));
        _factory.SetupGet("api/status/", """{"pngx_version":"2.20.15"}""");
        _factory.MockHandler
            .When(HttpMethod.Patch, "https://paperless.example.com/api/custom_fields/1/")
            .WithJsonContent<JsonElement>(json => HasObjectSelectOptions(
                json,
                new SelectOption { Id = "pending-id", Label = "Pending" },
                new SelectOption { Id = "approved-id", Label = "Approved" },
                new SelectOption { Label = "Rejected" }))
            .Respond("application/json", JsonSerializer.Serialize(updatedField));

        // Act
        var result = await CustomFieldTools.Update(
            _factory.Client,
            1,
            selectOptions: "Pending,Approved,Rejected");

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue("because response was {0}", result);
        json.RootElement.GetProperty("result")
            .GetProperty("extra_data")
            .GetProperty("select_options")[2]
            .GetProperty("id")
            .GetString()
            .Should().Be("rejected-id");
    }

    [Fact]
    public async Task Update_SelectField_BeforeV214_UsesStringOptions()
    {
        // Arrange
        const string existingField =
            """{"id":1,"name":"Status","data_type":"select","extra_data":{"select_options":["Pending","Approved"]}}""";
        const string updatedField =
            """{"id":1,"name":"Status","data_type":"select","extra_data":{"select_options":["Pending","Rejected"]}}""";

        _factory.SetupGet("api/custom_fields/1/", existingField);
        _factory.SetupGet("api/status/", """{"pngx_version":"2.13.5"}""");
        _factory.MockHandler
            .When(HttpMethod.Patch, "https://paperless.example.com/api/custom_fields/1/")
            .WithJsonContent<JsonElement>(json => HasStringSelectOptions(json, "Pending", "Rejected"))
            .Respond("application/json", updatedField);

        // Act
        var result = await CustomFieldTools.Update(
            _factory.Client,
            1,
            selectOptions: "Pending,Rejected");

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result")
            .GetProperty("extra_data")
            .GetProperty("select_options")[1]
            .GetProperty("label")
            .GetString()
            .Should().Be("Rejected");
    }

    [Fact]
    public async Task Delete_WithoutConfirmation_ReturnsDryRun()
    {
        // Arrange
        _factory.SetupGet("api/custom_fields/1/", TestFixtures.CustomFields.CreateCustomFieldJson(1, "To Delete"));

        // Act
        var result = await CustomFieldTools.Delete(_factory.Client, 1, confirm: false);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("CONFIRMATION_REQUIRED");
    }

    [Fact]
    public async Task Delete_WithConfirmation_DeletesField()
    {
        // Arrange
        _factory.SetupDelete("api/custom_fields/1/", HttpStatusCode.NoContent);

        // Act
        var result = await CustomFieldTools.Delete(_factory.Client, 1, confirm: true);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result").GetProperty("deleted").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Assign_WhenDocumentExists_AssignsFieldValue()
    {
        // Arrange
        _factory.SetupGet("api/documents/1/", TestFixtures.Documents.CreateDocumentJson(1, "Test Doc"));
        _factory.SetupGet("api/custom_fields/1/", TestFixtures.CustomFields.CreateCustomFieldJson(1, "Invoice Number"));
        _factory.SetupPatch("api/documents/1/", TestFixtures.Documents.CreateDocumentJson(1, "Test Doc"));

        // Act
        var result = await CustomFieldTools.Assign(_factory.Client, documentId: 1, fieldId: 1, value: "INV-001");

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result").GetProperty("document_id").GetInt32().Should().Be(1);
        json.RootElement.GetProperty("result").GetProperty("field_id").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Assign_WhenDocumentNotFound_ReturnsError()
    {
        // Arrange
        _factory.SetupGetWithStatus("api/documents/999/", HttpStatusCode.NotFound);

        // Act
        var result = await CustomFieldTools.Assign(_factory.Client, documentId: 999, fieldId: 1, value: "test");

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Assign_DocumentLinkField_SendsIdArray()
    {
        // Arrange
        _factory.SetupGet("api/documents/1/", TestFixtures.Documents.CreateDocumentJson(1, "Test Doc"));
        _factory.SetupGet("api/custom_fields/1/", TestFixtures.CustomFields.CreateCustomFieldJson(1, "Related Documents", "documentlink"));
        _factory.MockHandler
            .When(HttpMethod.Patch, "https://paperless.example.com/api/documents/1/")
            .WithJsonContent<JsonElement>(json =>
                json.GetProperty("custom_fields")[0].GetProperty("value").EnumerateArray()
                    .Select(v => v.GetInt32()).SequenceEqual([3, 7, 12]))
            .Respond("application/json", TestFixtures.Documents.CreateDocumentJson(1, "Test Doc"));

        // Act
        var result = await CustomFieldTools.Assign(_factory.Client, documentId: 1, fieldId: 1, value: "3, 7, 12");

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue("because response was {0}", result);
        json.RootElement.GetProperty("result").GetProperty("value").EnumerateArray()
            .Select(v => v.GetInt32()).Should().Equal(3, 7, 12);
    }

    [Fact]
    public async Task Assign_DocumentLinkField_WithInvalidId_OmitsValue()
    {
        // Arrange: an unparsable ID yields a null value, and the client's serializer
        // (DefaultIgnoreCondition = WhenWritingNull) omits null properties entirely.
        _factory.SetupGet("api/documents/1/", TestFixtures.Documents.CreateDocumentJson(1, "Test Doc"));
        _factory.SetupGet("api/custom_fields/1/", TestFixtures.CustomFields.CreateCustomFieldJson(1, "Related Documents", "documentlink"));
        _factory.MockHandler
            .When(HttpMethod.Patch, "https://paperless.example.com/api/documents/1/")
            .WithJsonContent<JsonElement>(json =>
                !json.GetProperty("custom_fields")[0].TryGetProperty("value", out _))
            .Respond("application/json", TestFixtures.Documents.CreateDocumentJson(1, "Test Doc"));

        // Act
        var result = await CustomFieldTools.Assign(_factory.Client, documentId: 1, fieldId: 1, value: "3, abc");

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue("because response was {0}", result);
        json.RootElement.GetProperty("result").GetProperty("value").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Assign_WhenFieldNotFound_ReturnsError()
    {
        // Arrange
        _factory.SetupGet("api/documents/1/", TestFixtures.Documents.CreateDocumentJson(1, "Test Doc"));
        _factory.SetupGetWithStatus("api/custom_fields/999/", HttpStatusCode.NotFound);

        // Act
        var result = await CustomFieldTools.Assign(_factory.Client, documentId: 1, fieldId: 999, value: "test");

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("NOT_FOUND");
    }

    private static bool HasStringSelectOptions(JsonElement request, params string[] expectedLabels)
    {
        var options = request.GetProperty("extra_data").GetProperty("select_options").EnumerateArray().ToArray();
        return options.Length == expectedLabels.Length
               && options.Select(option => option.GetString()).SequenceEqual(expectedLabels);
    }

    private static bool HasObjectSelectOptions(JsonElement request, params SelectOption[] expectedOptions)
    {
        var options = request.GetProperty("extra_data").GetProperty("select_options").EnumerateArray().ToArray();
        if (options.Length != expectedOptions.Length)
        {
            return false;
        }

        for (var index = 0; index < options.Length; index++)
        {
            var option = options[index];
            var expected = expectedOptions[index];
            if (option.GetProperty("label").GetString() != expected.Label)
            {
                return false;
            }

            if (expected.Id == null)
            {
                if (option.TryGetProperty("id", out _))
                {
                    return false;
                }
            }
            else if (!option.TryGetProperty("id", out var id) || id.GetString() != expected.Id)
            {
                return false;
            }
        }

        return true;
    }
}
