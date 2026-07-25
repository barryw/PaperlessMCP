using System.Net;
using System.Text.Json;
using FluentAssertions;
using PaperlessMCP.Tests.Fixtures;
using RichardSzalay.MockHttp;
using PaperlessMCP.Tools;
using Xunit;

namespace PaperlessMCP.Tests.Tools;

public class DocumentToolsTests : IDisposable
{
    private readonly MockHttpClientFactory _factory;

    public DocumentToolsTests()
    {
        _factory = new MockHttpClientFactory();
    }

    public void Dispose()
    {
        _factory.Dispose();
    }

    #region Search Tests

    [Fact]
    public async Task Search_WithQuery_ReturnsResults()
    {
        // Arrange
        _factory.MockHandler
            .When(HttpMethod.Get, "https://paperless.example.com/api/documents/*")
            .Respond("application/json", TestFixtures.Documents.CreateSearchResultsJson(5));

        // Act
        var result = await DocumentTools.Search(_factory.Client, query: "invoice");

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result").GetArrayLength().Should().Be(5);
        json.RootElement.GetProperty("meta").GetProperty("total").GetInt32().Should().Be(5);
    }

    [Fact]
    public async Task Search_WithPagination_IncludesMetadata()
    {
        // Arrange
        _factory.MockHandler
            .When(HttpMethod.Get, "https://paperless.example.com/api/documents/*")
            .Respond("application/json", TestFixtures.Documents.CreateSearchResultsJson(50));

        // Act
        var result = await DocumentTools.Search(_factory.Client, page: 2, pageSize: 10);

        // Assert
        var json = JsonDocument.Parse(result);
        var meta = json.RootElement.GetProperty("meta");
        meta.GetProperty("page").GetInt32().Should().Be(2);
        meta.GetProperty("page_size").GetInt32().Should().Be(10);
    }

    [Fact]
    public async Task Search_WithFilters_PassesFiltersCorrectly()
    {
        // Arrange
        _factory.MockHandler
            .When(HttpMethod.Get, "https://paperless.example.com/api/documents/*")
            .Respond("application/json", TestFixtures.Documents.CreateSearchResultsJson(2));

        // Act
        var result = await DocumentTools.Search(
            _factory.Client,
            query: "test",
            tags: "1,2",
            correspondent: 3,
            documentType: 4,
            createdAfter: "2024-01-01",
            createdBefore: "2024-12-31"
        );

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Search_WithCorrespondentAndNoteUserObject_ReturnsDocuments()
    {
        // Arrange
        string? requestedPathAndQuery = null;
        const string responseJson = """
            {
              "count": 1,
              "next": null,
              "previous": null,
              "results": [
                {
                  "id": 42,
                  "correspondent": 17,
                  "document_type": 1,
                  "storage_path": null,
                  "title": "Corsair Invoice",
                  "content": "Corsair",
                  "tags": [],
                  "created": "2026-06-01",
                  "created_date": "2026-06-01",
                  "modified": "2026-06-02T10:00:00Z",
                  "added": "2026-06-02T10:00:00Z",
                  "archive_serial_number": null,
                  "original_file_name": "corsair.pdf",
                  "archived_file_name": null,
                  "owner": 1,
                  "custom_fields": [],
                  "notes": [
                    {
                      "id": 9,
                      "note": "Reviewed",
                      "created": "2026-06-02T11:00:00Z",
                      "user": {
                        "id": 2,
                        "username": "alice",
                        "first_name": "Alice",
                        "last_name": "Doe"
                      }
                    }
                  ]
                }
              ]
            }
            """;

        _factory.MockHandler
            .When(HttpMethod.Get, "https://paperless.example.com/api/documents/*")
            .With(request =>
            {
                requestedPathAndQuery = request.RequestUri?.PathAndQuery;
                return true;
            })
            .Respond("application/json", responseJson);

        // Act
        var result = await DocumentTools.Search(_factory.Client, correspondent: 17);

        // Assert
        requestedPathAndQuery.Should().Be("/api/documents/?correspondent__id=17&page=1&page_size=25");
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("meta").GetProperty("total").GetInt32().Should().Be(1);
        json.RootElement.GetProperty("result").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Search_WhenPaperlessResponseIsIncompatible_ReturnsUpstreamError()
    {
        // Arrange
        const string responseJson = """
            {
              "count": 1,
              "next": null,
              "previous": null,
              "results": [
                {
                  "id": "not-an-integer",
                  "title": "Invalid document shape"
                }
              ]
            }
            """;

        _factory.MockHandler
            .When(HttpMethod.Get, "https://paperless.example.com/api/documents/*")
            .Respond("application/json", responseJson);

        // Act
        var result = await DocumentTools.Search(_factory.Client, correspondent: 17);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("UPSTREAM_ERROR");
        json.RootElement.GetProperty("error")
            .GetProperty("details")
            .GetProperty("status_code")
            .GetInt32()
            .Should().Be(502);
    }

    [Fact]
    public async Task Search_ByDefault_ExcludesContent()
    {
        // Arrange
        _factory.MockHandler
            .When(HttpMethod.Get, "https://paperless.example.com/api/documents/*")
            .Respond("application/json", TestFixtures.Documents.CreateSearchResultsJson(2));

        // Act
        var result = await DocumentTools.Search(_factory.Client, query: "test");

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();

        var results = json.RootElement.GetProperty("result");
        results.GetArrayLength().Should().Be(2);

        // Content should be null when includeContent is false (default)
        foreach (var doc in results.EnumerateArray())
        {
            doc.GetProperty("content").ValueKind.Should().Be(JsonValueKind.Null);
        }
    }

    [Fact]
    public async Task Search_WithIncludeContent_ReturnsContent()
    {
        // Arrange
        var longContent = TestFixtures.Documents.CreateLongContent(1000);
        _factory.MockHandler
            .When(HttpMethod.Get, "https://paperless.example.com/api/documents/*")
            .Respond("application/json", TestFixtures.Documents.CreateSearchResultsJson(2, longContent));

        // Act
        var result = await DocumentTools.Search(
            _factory.Client,
            query: "test",
            includeContent: true,
            contentMaxLength: 0); // Unlimited

        // Assert
        var json = JsonDocument.Parse(result);
        var results = json.RootElement.GetProperty("result");

        foreach (var doc in results.EnumerateArray())
        {
            var content = doc.GetProperty("content").GetString();
            content.Should().NotBeNullOrEmpty();
            content.Should().Be(longContent);
        }
    }

    [Fact]
    public async Task Search_WithContentMaxLength_TruncatesContent()
    {
        // Arrange
        var longContent = TestFixtures.Documents.CreateLongContent(1000);
        _factory.MockHandler
            .When(HttpMethod.Get, "https://paperless.example.com/api/documents/*")
            .Respond("application/json", TestFixtures.Documents.CreateSearchResultsJson(2, longContent));

        // Act
        var result = await DocumentTools.Search(
            _factory.Client,
            query: "test",
            includeContent: true,
            contentMaxLength: 100);

        // Assert
        var json = JsonDocument.Parse(result);
        var results = json.RootElement.GetProperty("result");

        foreach (var doc in results.EnumerateArray())
        {
            var content = doc.GetProperty("content").GetString();
            content.Should().NotBeNullOrEmpty();
            content!.Length.Should().Be(103); // 100 chars + "..."
            content.Should().EndWith("...");
        }
    }

    [Fact]
    public async Task Search_ReturnsDocumentSummaryFields()
    {
        // Arrange
        _factory.MockHandler
            .When(HttpMethod.Get, "https://paperless.example.com/api/documents/*")
            .Respond("application/json", TestFixtures.Documents.CreateSearchResultsJson(1));

        // Act
        var result = await DocumentTools.Search(_factory.Client, query: "test");

        // Assert
        var json = JsonDocument.Parse(result);
        var doc = json.RootElement.GetProperty("result")[0];

        // DocumentSummary fields should be present
        doc.GetProperty("id").GetInt32().Should().BeGreaterThan(0);
        doc.GetProperty("title").GetString().Should().NotBeNullOrEmpty();
        doc.GetProperty("correspondent").ValueKind.Should().NotBe(JsonValueKind.Undefined);
        doc.GetProperty("document_type").ValueKind.Should().NotBe(JsonValueKind.Undefined);
        doc.GetProperty("tags").GetArrayLength().Should().BeGreaterThanOrEqualTo(0);
        doc.GetProperty("created").ValueKind.Should().NotBe(JsonValueKind.Undefined);

        // SearchHit should be present
        doc.GetProperty("__search_hit__").GetProperty("score").GetDouble().Should().BeGreaterThan(0);
    }

    #endregion

    #region Get Tests

    [Fact]
    public async Task Get_WhenDocumentExists_ReturnsDocument()
    {
        // Arrange
        _factory.SetupGet("api/documents/1/", TestFixtures.Documents.CreateDocumentJson(1, "Test Invoice"));

        // Act
        var result = await DocumentTools.Get(_factory.Client, 1);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result").GetProperty("id").GetInt32().Should().Be(1);
        json.RootElement.GetProperty("result").GetProperty("title").GetString().Should().Be("Test Invoice");
    }

    [Fact]
    public async Task Get_WhenDocumentNotFound_ReturnsError()
    {
        // Arrange
        _factory.SetupGetWithStatus("api/documents/999/", HttpStatusCode.NotFound);

        // Act
        var result = await DocumentTools.Get(_factory.Client, 999);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("NOT_FOUND");
    }

    #endregion

    #region Download Tests

    [Fact]
    public async Task Download_WhenDocumentExists_ReturnsDownloadUrls()
    {
        // Arrange
        _factory.SetupGet("api/documents/1/", TestFixtures.Documents.CreateDocumentJson(1, "Test Doc"));

        // Act
        var result = await DocumentTools.Download(_factory.Client, 1);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();

        var downloadResult = json.RootElement.GetProperty("result");
        downloadResult.GetProperty("download_url").GetString().Should().Contain("/api/documents/1/download/");
        downloadResult.GetProperty("preview_url").GetString().Should().Contain("/api/documents/1/preview/");
        downloadResult.GetProperty("thumbnail_url").GetString().Should().Contain("/api/documents/1/thumb/");
    }

    [Fact]
    public async Task Preview_WhenDocumentExists_ReturnsPreviewUrl()
    {
        // Arrange
        _factory.SetupGet("api/documents/1/", TestFixtures.Documents.CreateDocumentJson(1, "Test Doc"));

        // Act
        var result = await DocumentTools.Preview(_factory.Client, 1);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result").GetProperty("preview_url").GetString()
            .Should().Contain("/api/documents/1/preview/");
    }

    [Fact]
    public async Task Thumbnail_WhenDocumentExists_ReturnsThumbnailUrl()
    {
        // Arrange
        _factory.SetupGet("api/documents/1/", TestFixtures.Documents.CreateDocumentJson(1, "Test Doc"));

        // Act
        var result = await DocumentTools.Thumbnail(_factory.Client, 1);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result").GetProperty("thumbnail_url").GetString()
            .Should().Contain("/api/documents/1/thumb/");
    }

    #endregion

    #region Upload Tests

    [Fact]
    public async Task Upload_WithValidBase64_ReturnsTaskId()
    {
        // Arrange
        var fileContent = Convert.ToBase64String("Test file content"u8.ToArray());
        _factory.MockHandler
            .When(HttpMethod.Post, "https://paperless.example.com/api/documents/post_document/")
            .Respond("application/json", "\"task-uuid-12345\"");

        // Act
        var result = await DocumentTools.Upload(
            _factory.Client,
            fileContent,
            "test.pdf",
            title: "Test Upload");

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result").GetProperty("task_id").GetString().Should().Be("task-uuid-12345");
    }

    [Fact]
    public async Task Upload_WithInvalidBase64_ReturnsValidationError()
    {
        // Act
        var result = await DocumentTools.Upload(
            _factory.Client,
            "not-valid-base64!!!",
            "test.pdf");

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION");
    }

    [Fact]
    public async Task UploadFromPath_WhenFileNotFound_ReturnsError()
    {
        // Act
        var result = await DocumentTools.UploadFromPath(
            _factory.Client,
            "/nonexistent/path/to/file.pdf");

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task UploadFromPath_WithRelativePath_ReturnsValidationError()
    {
        // Act
        var result = await DocumentTools.UploadFromPath(
            _factory.Client,
            "relative/path/to/file.pdf");

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION");
    }

    [Fact]
    public async Task UploadFromPath_WithValidFile_ReturnsTaskId()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "Test file content for upload");

            _factory.MockHandler
                .When(HttpMethod.Post, "https://paperless.example.com/api/documents/post_document/")
                .Respond("application/json", "\"task-uuid-from-path-12345\"");

            // Act
            var result = await DocumentTools.UploadFromPath(
                _factory.Client,
                tempFile,
                title: "Test Path Upload");

            // Assert
            var json = JsonDocument.Parse(result);
            json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
            json.RootElement.GetProperty("result").GetProperty("task_id").GetString().Should().Be("task-uuid-from-path-12345");
            json.RootElement.GetProperty("result").GetProperty("file_name").GetString().Should().NotBeNullOrEmpty();
            json.RootElement.GetProperty("result").GetProperty("file_size").GetInt64().Should().BeGreaterThan(0);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task UploadFromPath_ExpandsTildeToHome()
    {
        // This test verifies tilde expansion happens (even if file doesn't exist)
        // Act
        var result = await DocumentTools.UploadFromPath(
            _factory.Client,
            "~/nonexistent_test_file_12345.pdf");

        // Assert - Should try to find the file (and fail with NOT_FOUND, not VALIDATION)
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("NOT_FOUND");
    }

    #endregion

    #region Update Tests

    [Fact]
    public async Task Update_WhenSuccessful_ReturnsUpdatedDocument()
    {
        // Arrange
        _factory.SetupPatch("api/documents/1/", TestFixtures.Documents.CreateDocumentJson(1, "Updated Title"));

        // Act
        var result = await DocumentTools.Update(_factory.Client, 1, title: "Updated Title");

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result").GetProperty("title").GetString().Should().Be("Updated Title");
    }

    [Fact]
    public async Task Update_ByDefault_OmitsContentToSaveTokens()
    {
        // Arrange - fixture document carries full OCR content
        _factory.SetupPatch("api/documents/1/", TestFixtures.Documents.CreateDocumentJson(1, "Updated Title"));

        // Act - default call (no includeContent)
        var result = await DocumentTools.Update(_factory.Client, 1, title: "Updated Title");

        // Assert - metadata present, content suppressed
        var json = JsonDocument.Parse(result);
        var resultElement = json.RootElement.GetProperty("result");
        resultElement.GetProperty("title").GetString().Should().Be("Updated Title");

        var hasContent = resultElement.TryGetProperty("content", out var content);
        (hasContent == false || string.IsNullOrEmpty(content.GetString()))
            .Should().BeTrue("update responses should not echo the full OCR content by default");
    }

    [Fact]
    public async Task Update_WithIncludeContent_ReturnsFullContent()
    {
        // Arrange
        _factory.SetupPatch("api/documents/1/", TestFixtures.Documents.CreateDocumentJson(1, "Updated Title"));

        // Act
        var result = await DocumentTools.Update(_factory.Client, 1, title: "Updated Title", includeContent: true);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("result").GetProperty("content").GetString()
            .Should().Be("This is test content for the document.");
    }

    [Fact]
    public async Task Update_WhenNotFound_ReturnsError()
    {
        // Arrange
        _factory.SetupPatchWithStatus("api/documents/999/", HttpStatusCode.NotFound);

        // Act
        var result = await DocumentTools.Update(_factory.Client, 999, title: "New Title");

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Update_WhenBadRequest_ReturnsErrorWithDetails()
    {
        // Arrange
        var errorBody = """{"title": ["This field may not be blank."]}""";
        _factory.SetupPatchWithError("api/documents/1/", HttpStatusCode.BadRequest, errorBody);

        // Act
        var result = await DocumentTools.Update(_factory.Client, 1, title: "");

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("UPSTREAM_ERROR");

        // Verify error details include status code and response body
        var details = json.RootElement.GetProperty("error").GetProperty("details");
        details.GetProperty("status_code").GetInt32().Should().Be(400);
        details.GetProperty("response_body").GetString().Should().Contain("This field may not be blank");
    }

    [Fact]
    public async Task Update_WhenForbidden_ReturnsErrorWithDetails()
    {
        // Arrange
        var errorBody = """{"detail": "You do not have permission to perform this action."}""";
        _factory.SetupPatchWithError("api/documents/1/", HttpStatusCode.Forbidden, errorBody);

        // Act
        var result = await DocumentTools.Update(_factory.Client, 1, title: "Updated");

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("UPSTREAM_ERROR");

        var details = json.RootElement.GetProperty("error").GetProperty("details");
        details.GetProperty("status_code").GetInt32().Should().Be(403);
        details.GetProperty("response_body").GetString().Should().Contain("permission");
    }

    #endregion

    #region Delete Tests

    [Fact]
    public async Task Delete_WithoutConfirmation_ReturnsDryRun()
    {
        // Arrange
        _factory.SetupGet("api/documents/1/", TestFixtures.Documents.CreateDocumentJson(1, "Doc to Delete"));

        // Act
        var result = await DocumentTools.Delete(_factory.Client, 1, confirm: false);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("CONFIRMATION_REQUIRED");
    }

    [Fact]
    public async Task Delete_WithConfirmation_DeletesDocument()
    {
        // Arrange
        _factory.SetupDelete("api/documents/1/", HttpStatusCode.NoContent);

        // Act
        var result = await DocumentTools.Delete(_factory.Client, 1, confirm: true);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result").GetProperty("deleted").GetBoolean().Should().BeTrue();
    }

    #endregion

    #region Bulk Update Tests

    [Fact]
    public async Task BulkUpdate_WithDryRun_ReturnsPreview()
    {
        // Act
        var result = await DocumentTools.BulkUpdate(
            _factory.Client,
            documentIds: "1,2,3",
            operation: "add_tag",
            value: 5,
            dryRun: true,
            confirm: false);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result").GetProperty("executed").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task BulkUpdate_WithConfirmation_ExecutesOperation()
    {
        // Arrange
        _factory.SetupPost("api/documents/bulk_edit/", "{}");

        // Act
        var result = await DocumentTools.BulkUpdate(
            _factory.Client,
            documentIds: "1,2,3",
            operation: "add_tag",
            value: 5,
            dryRun: false,
            confirm: true);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result").GetProperty("executed").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task BulkUpdate_WhenUpstreamFails_ReturnsErrorDetails()
    {
        // Arrange
        const string errorBody = "{\"detail\":\"Invalid tag ID\"}";
        _factory.SetupPostWithError("api/documents/bulk_edit/", HttpStatusCode.BadRequest, errorBody);

        // Act
        var result = await DocumentTools.BulkUpdate(
            _factory.Client,
            documentIds: "1,2,3",
            operation: "add_tag",
            value: 999,
            dryRun: false,
            confirm: true);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("message").GetString()
            .Should().Contain("HTTP 400").And.Contain("Invalid tag ID");
    }

    [Fact]
    public async Task BulkUpdate_WithInvalidOperation_ReturnsValidationError()
    {
        // Act
        var result = await DocumentTools.BulkUpdate(
            _factory.Client,
            documentIds: "1,2,3",
            operation: "invalid_operation",
            dryRun: false,
            confirm: true);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION");
    }

    [Fact]
    public async Task BulkUpdate_WithEmptyIds_ReturnsValidationError()
    {
        // Act
        var result = await DocumentTools.BulkUpdate(
            _factory.Client,
            documentIds: "",
            operation: "add_tag",
            dryRun: false,
            confirm: true);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION");
    }

    #endregion

    #region Reprocess Tests

    [Fact]
    public async Task Reprocess_WithoutConfirmation_ReturnsDryRun()
    {
        // Arrange
        _factory.SetupGet("api/documents/1/", TestFixtures.Documents.CreateDocumentJson(1, "Doc to Reprocess"));

        // Act
        var result = await DocumentTools.Reprocess(_factory.Client, 1, confirm: false);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("CONFIRMATION_REQUIRED");
    }

    [Fact]
    public async Task Reprocess_WithConfirmation_QueuesReprocessing()
    {
        // Arrange
        _factory.SetupPost("api/documents/bulk_edit/", "{}");

        // Act
        var result = await DocumentTools.Reprocess(_factory.Client, 1, confirm: true);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result").GetProperty("status").GetString().Should().Be("queued");
    }

    [Fact]
    public async Task Reprocess_WhenUpstreamFails_ReturnsErrorDetails()
    {
        // Arrange
        const string errorBody = "{\"detail\":\"Document cannot be reprocessed\"}";
        _factory.SetupPostWithError("api/documents/bulk_edit/", HttpStatusCode.BadRequest, errorBody);

        // Act
        var result = await DocumentTools.Reprocess(_factory.Client, 1, confirm: true);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("message").GetString()
            .Should().Contain("HTTP 400").And.Contain("Document cannot be reprocessed");
    }

    #endregion

    #region Title Length Validation Tests

    // Paperless-ngx stores at most 127 characters of a document title: the model says
    // max_length=128, but the consumer does Document.objects.create(title=title[:127]),
    // and the upload serializer runs no length validation at all. A longer title is
    // therefore truncated silently while the API reports success. These tests pin the
    // boundary at 127 across all three write paths.

    private const int TitleLimit = 127;
    private static string TitleOfLength(int length) => new('A', length);

    [Fact]
    public async Task Upload_WithTitleAtLimit_Succeeds()
    {
        // Arrange
        var fileContent = Convert.ToBase64String("Test file content"u8.ToArray());
        _factory.MockHandler
            .When(HttpMethod.Post, "https://paperless.example.com/api/documents/post_document/")
            .Respond("application/json", "\"task-uuid-12345\"");

        // Act
        var result = await DocumentTools.Upload(
            _factory.Client,
            fileContent,
            "test.pdf",
            title: TitleOfLength(TitleLimit));

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("warnings").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Upload_WithTitleOverLimit_ReturnsValidationErrorAndSendsNothing()
    {
        // Arrange
        var fileContent = Convert.ToBase64String("Test file content"u8.ToArray());
        var upload = _factory.MockHandler
            .When(HttpMethod.Post, "https://paperless.example.com/api/documents/post_document/")
            .Respond("application/json", "\"task-uuid-12345\"");

        // Act
        var result = await DocumentTools.Upload(
            _factory.Client,
            fileContent,
            "test.pdf",
            title: TitleOfLength(TitleLimit + 1));

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION");
        _factory.MockHandler.GetMatchCount(upload).Should().Be(0, "nothing may reach Paperless when the title is rejected");
    }

    [Fact]
    public async Task UploadFromPath_WithTitleAtLimit_Succeeds()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "Test file content for upload");
            _factory.MockHandler
                .When(HttpMethod.Post, "https://paperless.example.com/api/documents/post_document/")
                .Respond("application/json", "\"task-uuid-12345\"");

            // Act
            var result = await DocumentTools.UploadFromPath(
                _factory.Client,
                tempFile,
                title: TitleOfLength(TitleLimit));

            // Assert
            var json = JsonDocument.Parse(result);
            json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
            json.RootElement.GetProperty("warnings").GetArrayLength().Should().Be(0);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task UploadFromPath_WithTitleOverLimit_ReturnsValidationErrorAndSendsNothing()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "Test file content for upload");
            var upload = _factory.MockHandler
                .When(HttpMethod.Post, "https://paperless.example.com/api/documents/post_document/")
                .Respond("application/json", "\"task-uuid-12345\"");

            // Act
            var result = await DocumentTools.UploadFromPath(
                _factory.Client,
                tempFile,
                title: TitleOfLength(TitleLimit + 1));

            // Assert
            var json = JsonDocument.Parse(result);
            json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
            json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION");
            _factory.MockHandler.GetMatchCount(upload).Should().Be(0, "nothing may reach Paperless when the title is rejected");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task UploadFromPath_WithDerivedTitleOverLimit_ReturnsValidationError()
    {
        // A title derived from an over-long file name is just as vulnerable to silent
        // truncation as one passed explicitly, so it must be rejected too.
        // Arrange
        var longName = TitleOfLength(TitleLimit + 1) + ".pdf";
        var tempFile = Path.Combine(Path.GetTempPath(), longName);
        try
        {
            await File.WriteAllTextAsync(tempFile, "Test file content for upload");

            // Act
            var result = await DocumentTools.UploadFromPath(_factory.Client, tempFile);

            // Assert
            var json = JsonDocument.Parse(result);
            json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
            json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Update_WithTitleAtLimit_Succeeds()
    {
        // Arrange
        var title = TitleOfLength(TitleLimit);
        _factory.SetupPatch("api/documents/1/", TestFixtures.Documents.CreateDocumentJson(1, title));

        // Act
        var result = await DocumentTools.Update(_factory.Client, 1, title: title);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result").GetProperty("title").GetString().Should().Be(title);
    }

    [Fact]
    public async Task Update_WithTitleOverLimit_ReturnsValidationErrorAndSendsNothing()
    {
        // Arrange
        var patch = _factory.SetupPatch("api/documents/1/", TestFixtures.Documents.CreateDocumentJson(1, "unused"));

        // Act
        var result = await DocumentTools.Update(_factory.Client, 1, title: TitleOfLength(TitleLimit + 1));

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION");
        _factory.MockHandler.GetMatchCount(patch).Should().Be(0, "the document must be left untouched when the title is rejected");
    }

    [Fact]
    public async Task Update_WithTitleOverLimit_ErrorMessageNamesLimitAndActualLength()
    {
        // Act
        var result = await DocumentTools.Update(_factory.Client, 1, title: TitleOfLength(140));

        // Assert
        var json = JsonDocument.Parse(result);
        var message = json.RootElement.GetProperty("error").GetProperty("message").GetString();
        message.Should().Contain("140", "the caller needs to know how long their title actually was");
        message.Should().Contain("127", "the caller needs to know the limit");
    }

    [Fact]
    public async Task Update_WithNullTitle_IsNotRejected()
    {
        // A null title means "leave it alone" and must not trip the length check.
        // Arrange
        _factory.SetupPatch("api/documents/1/", TestFixtures.Documents.CreateDocumentJson(1, "Untouched"));

        // Act
        var result = await DocumentTools.Update(_factory.Client, 1, correspondent: 5);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
    }

    // --- Effective-title validation: null/empty titles fall back to the filename stem
    // (PaperlessClient omits null/empty titles from the request, so Paperless derives
    // the title from the file name server-side and truncates it just the same). ---

    [Fact]
    public async Task Upload_WithNullTitleAndShortFileName_Succeeds()
    {
        // Regression guard for the base64-null path: no title plus a short filename
        // must keep working exactly as before.
        // Arrange
        var fileContent = Convert.ToBase64String("Test file content"u8.ToArray());
        _factory.MockHandler
            .When(HttpMethod.Post, "https://paperless.example.com/api/documents/post_document/")
            .Respond("application/json", "\"task-uuid-12345\"");

        // Act
        var result = await DocumentTools.Upload(_factory.Client, fileContent, "test.pdf");

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("warnings").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Upload_WithNullTitleAndOverlongFileNameStem_ReturnsValidationError()
    {
        // Arrange
        var fileContent = Convert.ToBase64String("Test file content"u8.ToArray());
        var upload = _factory.MockHandler
            .When(HttpMethod.Post, "https://paperless.example.com/api/documents/post_document/")
            .Respond("application/json", "\"task-uuid-12345\"");
        var fileName = TitleOfLength(TitleLimit + 1) + ".pdf";

        // Act
        var result = await DocumentTools.Upload(_factory.Client, fileContent, fileName);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION");
        _factory.MockHandler.GetMatchCount(upload).Should().Be(0, "nothing may reach Paperless when the derived title is rejected");
    }

    [Fact]
    public async Task Upload_WithEmptyTitleAndOverlongFileNameStem_ReturnsValidationError()
    {
        // An empty title is omitted by PaperlessClient exactly like null, so it must
        // get the same filename fallback and the same rejection.
        // Arrange
        var fileContent = Convert.ToBase64String("Test file content"u8.ToArray());
        var fileName = TitleOfLength(TitleLimit + 1) + ".pdf";

        // Act
        var result = await DocumentTools.Upload(_factory.Client, fileContent, fileName, title: "");

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION");
    }

    [Fact]
    public async Task UploadFromPath_WithEmptyTitleAndOverlongFileNameStem_ReturnsValidationError()
    {
        // Arrange
        var longName = TitleOfLength(TitleLimit + 1) + ".pdf";
        var tempFile = Path.Combine(Path.GetTempPath(), longName);
        try
        {
            await File.WriteAllTextAsync(tempFile, "Test file content for upload");

            // Act
            var result = await DocumentTools.UploadFromPath(_factory.Client, tempFile, title: "");

            // Assert
            var json = JsonDocument.Parse(result);
            json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
            json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Upload_WithAstralTitleOf64CodePoints_Succeeds()
    {
        // End-to-end guard for code-point counting: 64 astral emoji are 128 UTF-16
        // units but only 64 code points; Paperless stores them intact, so the tool
        // must accept them.
        // Arrange
        var fileContent = Convert.ToBase64String("Test file content"u8.ToArray());
        _factory.MockHandler
            .When(HttpMethod.Post, "https://paperless.example.com/api/documents/post_document/")
            .Respond("application/json", "\"task-uuid-12345\"");
        var title = string.Concat(Enumerable.Repeat("\U0001F600", 64));
        title.Length.Should().Be(128, "precondition: astral emoji take two UTF-16 units each");

        // Act
        var result = await DocumentTools.Upload(_factory.Client, fileContent, "test.pdf", title: title);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("warnings").GetArrayLength().Should().Be(0);
    }


    // --- Long-dotfile regression: Path.GetFileNameWithoutExtension(".<128 chars>") is
    // empty, so the effective-title guard used to see an empty title and let the upload
    // through, while Paperless kept the whole dotfile name as the stem and truncated it
    // to 127. Both upload paths must reject it. ---

    [Fact]
    public async Task Upload_WithNullTitleAndOverlongDotfileName_ReturnsValidationError()
    {
        // Arrange
        var fileContent = Convert.ToBase64String("Test file content"u8.ToArray());
        var upload = _factory.MockHandler
            .When(HttpMethod.Post, "https://paperless.example.com/api/documents/post_document/")
            .Respond("application/json", "\"task-uuid-12345\"");
        var fileName = "." + TitleOfLength(TitleLimit + 1);

        // Act
        var result = await DocumentTools.Upload(_factory.Client, fileContent, fileName);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION");
        _factory.MockHandler.GetMatchCount(upload).Should().Be(0, "nothing may reach Paperless when the derived title is rejected");
    }

    [Fact]
    public async Task Upload_WithNullTitleAndOverlongNameEndingInDot_ReturnsValidationError()
    {
        // 127 characters plus a trailing dot: .NET reports a safe 127-character stem,
        // but pathlib keeps the dot and Paperless stores 128 -> silent truncation.
        // Arrange
        var fileContent = Convert.ToBase64String("Test file content"u8.ToArray());
        var upload = _factory.MockHandler
            .When(HttpMethod.Post, "https://paperless.example.com/api/documents/post_document/")
            .Respond("application/json", "\"task-uuid-12345\"");
        var fileName = TitleOfLength(TitleLimit) + ".";

        // Act
        var result = await DocumentTools.Upload(_factory.Client, fileContent, fileName);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION");
        _factory.MockHandler.GetMatchCount(upload).Should().Be(0);
    }

    [Fact]
    public async Task Upload_WithNullTitleAndShortDotfileName_Succeeds()
    {
        // The guard must not overreach: an ordinary dotfile is a perfectly good title.
        // Arrange
        var fileContent = Convert.ToBase64String("Test file content"u8.ToArray());
        _factory.MockHandler
            .When(HttpMethod.Post, "https://paperless.example.com/api/documents/post_document/")
            .Respond("application/json", "\"task-uuid-12345\"");

        // Act
        var result = await DocumentTools.Upload(_factory.Client, fileContent, ".bashrc");

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("warnings").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task UploadFromPath_WithNullTitleAndOverlongDotfileName_ReturnsValidationError()
    {
        // Arrange
        var longDotfile = "." + TitleOfLength(TitleLimit + 1);
        var tempFile = Path.Combine(Path.GetTempPath(), longDotfile);
        try
        {
            await File.WriteAllTextAsync(tempFile, "Test file content for upload");
            var upload = _factory.MockHandler
                .When(HttpMethod.Post, "https://paperless.example.com/api/documents/post_document/")
                .Respond("application/json", "\"task-uuid-12345\"");

            // Act
            var result = await DocumentTools.UploadFromPath(_factory.Client, tempFile);

            // Assert
            var json = JsonDocument.Parse(result);
            json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
            json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION");
            _factory.MockHandler.GetMatchCount(upload).Should().Be(0, "nothing may reach Paperless when the derived title is rejected");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task UploadFromPath_WithNullTitleAndShortDotfileName_Succeeds()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), ".paperlessmcp-dotfile-probe");
        try
        {
            await File.WriteAllTextAsync(tempFile, "Test file content for upload");
            _factory.MockHandler
                .When(HttpMethod.Post, "https://paperless.example.com/api/documents/post_document/")
                .Respond("application/json", "\"task-uuid-12345\"");

            // Act
            var result = await DocumentTools.UploadFromPath(_factory.Client, tempFile);

            // Assert
            var json = JsonDocument.Parse(result);
            json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    #endregion
}
