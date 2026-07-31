using System.Net;
using System.Web;
using FluentAssertions;
using PaperlessMCP.Client;
using PaperlessMCP.Models.Correspondents;
using RichardSzalay.MockHttp;
using Xunit;
using PaperlessMCP.Models.CustomFields;
using PaperlessMCP.Models.DocumentTypes;
using PaperlessMCP.Models.StoragePaths;
using PaperlessMCP.Models.Documents;
using PaperlessMCP.Models.Tags;
using PaperlessMCP.Tests.Fixtures;

namespace PaperlessMCP.Tests.Client;

public class PaperlessClientTests : IDisposable
{
    private readonly MockHttpClientFactory _factory;

    public PaperlessClientTests()
    {
        _factory = new MockHttpClientFactory();
    }

    public void Dispose()
    {
        _factory.Dispose();
    }

    #region Ping Tests

    [Fact]
    public async Task PingAsync_WhenSuccessful_ReturnsSuccess()
    {
        // Arrange
        _factory.SetupGet("api/status/", """{"pngx_version": "2.0.0", "server_os": "Linux"}""");

        // Act
        var (success, version, error) = await _factory.Client.PingAsync();

        // Assert
        success.Should().BeTrue();
        version.Should().Be("2.0.0");
        error.Should().BeNull();
    }

    [Fact]
    public async Task PingAsync_WhenUnauthorized_ReturnsFailure()
    {
        // Arrange
        _factory.SetupGetWithStatus("api/status/", HttpStatusCode.Unauthorized);

        // Act
        var (success, version, error) = await _factory.Client.PingAsync();

        // Assert
        success.Should().BeFalse();
        version.Should().BeNull();
        error.Should().Contain("401");
    }

    #endregion

    #region Document Tests

    [Fact]
    public async Task SearchDocumentsAsync_WithQuery_ReturnsResults()
    {
        // Arrange
        _factory.MockHandler
            .When(HttpMethod.Get, "https://paperless.example.com/api/documents/*")
            .Respond("application/json", TestFixtures.Documents.CreateSearchResultsJson(5));

        // Act
        var result = await _factory.Client.SearchDocumentsAsync(query: "test");

        // Assert
        result.Should().NotBeNull();
        result.Count.Should().Be(5);
        result.Results.Should().HaveCount(5);
    }

    [Fact]
    public async Task SearchDocumentsAsync_WithFilters_ReturnsFilteredResults()
    {
        // Arrange
        _factory.MockHandler
            .When(HttpMethod.Get, "https://paperless.example.com/api/documents/*")
            .Respond("application/json", TestFixtures.Documents.CreateSearchResultsJson(2));

        // Act
        var result = await _factory.Client.SearchDocumentsAsync(
            query: "invoice",
            tags: [1, 2],
            correspondent: 3,
            documentType: 4);

        // Assert
        result.Should().NotBeNull();
        result.Count.Should().Be(2);
    }

    [Fact]
    public async Task SearchDocumentsAsync_WithCustomFieldQuery_SendsCustomFieldQueryParam()
    {
        // Arrange
        _factory.MockHandler
            .When(HttpMethod.Get, "https://paperless.example.com/api/documents/*")
            .With(request => HttpUtility.ParseQueryString(request.RequestUri!.Query)["custom_field_query"]
                == """["Invoice Number","icontains","INV-2024"]""")
            .Respond("application/json", TestFixtures.Documents.CreateSearchResultsJson(1));

        // Act
        var result = await _factory.Client.SearchDocumentsAsync(
            customFieldQuery: """["Invoice Number","icontains","INV-2024"]""");

        // Assert: an unmatched query string 404s, which surfaces as an empty result
        result.Count.Should().Be(1);
    }

    [Fact]
    public async Task SearchDocumentsAsync_WithoutCustomFieldQuery_OmitsCustomFieldQueryParam()
    {
        // Arrange
        _factory.MockHandler
            .When(HttpMethod.Get, "https://paperless.example.com/api/documents/*")
            .With(request => !request.RequestUri!.Query.Contains("custom_field_query"))
            .Respond("application/json", TestFixtures.Documents.CreateSearchResultsJson(2));

        // Act
        var result = await _factory.Client.SearchDocumentsAsync(query: "test");

        // Assert
        result.Count.Should().Be(2);
    }

    [Fact]
    public async Task GetDocumentAsync_WhenExists_ReturnsDocument()
    {
        // Arrange
        _factory.SetupGet("api/documents/1/", TestFixtures.Documents.CreateDocumentJson(1, "My Document"));

        // Act
        var result = await _factory.Client.GetDocumentAsync(1);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.Title.Should().Be("My Document");
    }

    [Fact]
    public async Task GetDocumentAsync_WhenNotFound_ReturnsNull()
    {
        // Arrange
        _factory.SetupGetWithStatus("api/documents/999/", HttpStatusCode.NotFound);

        // Act
        var result = await _factory.Client.GetDocumentAsync(999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteDocumentAsync_WhenSuccessful_ReturnsTrue()
    {
        // Arrange
        _factory.SetupDelete("api/documents/1/", HttpStatusCode.NoContent);

        // Act
        var result = await _factory.Client.DeleteDocumentAsync(1);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteDocumentAsync_WhenNotFound_ReturnsFalse()
    {
        // Arrange
        _factory.SetupDelete("api/documents/999/", HttpStatusCode.NotFound);

        // Act
        var result = await _factory.Client.DeleteDocumentAsync(999);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetDocumentDownloadInfo_ReturnsCorrectUrls()
    {
        // Act
        var result = _factory.Client.GetDocumentDownloadInfo(1, "Test Doc", "test.pdf");

        // Assert
        result.Id.Should().Be(1);
        result.Title.Should().Be("Test Doc");
        result.OriginalFileName.Should().Be("test.pdf");
        result.DownloadUrl.Should().Be("https://paperless.example.com/api/documents/1/download/");
        result.PreviewUrl.Should().Be("https://paperless.example.com/api/documents/1/preview/");
        result.ThumbnailUrl.Should().Be("https://paperless.example.com/api/documents/1/thumb/");
    }

    [Fact]
    public async Task UpdateDocumentWithResultAsync_WhenSuccessful_ReturnsSuccess()
    {
        // Arrange
        _factory.SetupPatch("api/documents/1/", TestFixtures.Documents.CreateDocumentJson(1, "Updated Title"));

        // Act
        var result = await _factory.Client.UpdateDocumentWithResultAsync(1, new DocumentUpdateRequest { Title = "Updated Title" });

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Title.Should().Be("Updated Title");
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task UpdateDocumentWithResultAsync_WhenNotFound_ReturnsFailureWithDetails()
    {
        // Arrange
        var errorBody = """{"detail": "Not found."}""";
        _factory.SetupPatchWithError("api/documents/999/", HttpStatusCode.NotFound, errorBody);

        // Act
        var result = await _factory.Client.UpdateDocumentWithResultAsync(999, new DocumentUpdateRequest { Title = "New Title" });

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Value.Should().BeNull();
        result.Error.Should().NotBeNull();
        result.Error!.StatusCode.Should().Be(HttpStatusCode.NotFound);
        result.Error.ResponseBody.Should().Be(errorBody);
    }

    [Fact]
    public async Task UpdateDocumentWithResultAsync_WhenBadRequest_ReturnsFailureWithDetails()
    {
        // Arrange
        var errorBody = """{"title": ["This field may not be blank."]}""";
        _factory.SetupPatchWithError("api/documents/1/", HttpStatusCode.BadRequest, errorBody);

        // Act
        var result = await _factory.Client.UpdateDocumentWithResultAsync(1, new DocumentUpdateRequest { Title = "" });

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        result.Error.ResponseBody.Should().Contain("This field may not be blank");
    }

    #endregion

    #region Tag Tests

    [Fact]
    public async Task GetTagsAsync_ReturnsTagList()
    {
        // Arrange
        _factory.MockHandler
            .When(HttpMethod.Get, "https://paperless.example.com/api/tags/*")
            .Respond("application/json", TestFixtures.Tags.CreateTagListJson(5));

        // Act
        var result = await _factory.Client.GetTagsAsync();

        // Assert
        result.Should().NotBeNull();
        result.Count.Should().Be(5);
        result.Results.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetTagAsync_WhenExists_ReturnsTag()
    {
        // Arrange
        _factory.SetupGet("api/tags/1/", TestFixtures.Tags.CreateTagJson(1, "Important"));

        // Act
        var result = await _factory.Client.GetTagAsync(1);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.Name.Should().Be("Important");
    }

    [Fact]
    public async Task CreateTagAsync_WhenSuccessful_ReturnsTag()
    {
        // Arrange
        _factory.SetupPost("api/tags/", TestFixtures.Tags.CreateTagJson(5, "New Tag"));

        // Act
        var result = await _factory.Client.CreateTagAsync(new TagCreateRequest { Name = "New Tag" });

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(5);
        result.Name.Should().Be("New Tag");
    }

    [Fact]
    public async Task DeleteTagAsync_WhenSuccessful_ReturnsTrue()
    {
        // Arrange
        _factory.SetupDelete("api/tags/1/", HttpStatusCode.NoContent);

        // Act
        var result = await _factory.Client.DeleteTagAsync(1);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task CreateTagWithResultAsync_WhenSuccessful_ReturnsSuccess()
    {
        // Arrange
        _factory.SetupPost("api/tags/", TestFixtures.Tags.CreateTagJson(5, "New Tag"));

        // Act
        var result = await _factory.Client.CreateTagWithResultAsync(new TagCreateRequest { Name = "New Tag" });

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Name.Should().Be("New Tag");
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task CreateTagWithResultAsync_WhenDuplicate_ReturnsFailureWithDetails()
    {
        // Arrange
        var errorBody = """{"name": ["tag with this name already exists."]}""";
        _factory.SetupPostWithError("api/tags/", HttpStatusCode.BadRequest, errorBody);

        // Act
        var result = await _factory.Client.CreateTagWithResultAsync(new TagCreateRequest { Name = "Existing Tag" });

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        result.Error.ResponseBody.Should().Contain("already exists");
    }

    [Fact]
    public async Task CreateTagWithResultAsync_WhenUnauthorized_ReturnsFailureWithDetails()
    {
        // Arrange
        var errorBody = """{"detail": "Authentication credentials were not provided."}""";
        _factory.SetupPostWithError("api/tags/", HttpStatusCode.Unauthorized, errorBody);

        // Act
        var result = await _factory.Client.CreateTagWithResultAsync(new TagCreateRequest { Name = "Test" });

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        result.Error.ResponseBody.Should().Contain("Authentication credentials");
    }

    #endregion

    #region Correspondent Tests

    [Fact]
    public async Task GetCorrespondentsAsync_ReturnsCorrespondentList()
    {
        // Arrange
        _factory.MockHandler
            .When(HttpMethod.Get, "https://paperless.example.com/api/correspondents/*")
            .Respond("application/json", TestFixtures.Correspondents.CreateCorrespondentListJson(3));

        // Act
        var result = await _factory.Client.GetCorrespondentsAsync();

        // Assert
        result.Should().NotBeNull();
        result.Count.Should().Be(3);
    }

    [Fact]
    public async Task CreateCorrespondentAsync_WhenSuccessful_ReturnsCorrespondent()
    {
        // Arrange
        _factory.SetupPost("api/correspondents/", TestFixtures.Correspondents.CreateCorrespondentJson(1, "ACME Corp"));

        // Act
        var result = await _factory.Client.CreateCorrespondentAsync(new CorrespondentCreateRequest { Name = "ACME Corp" });

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("ACME Corp");
    }

    #endregion

    #region Document Type Tests

    [Fact]
    public async Task GetDocumentTypesAsync_ReturnsDocumentTypeList()
    {
        // Arrange
        _factory.MockHandler
            .When(HttpMethod.Get, "https://paperless.example.com/api/document_types/*")
            .Respond("application/json", TestFixtures.DocumentTypes.CreateDocumentTypeListJson(4));

        // Act
        var result = await _factory.Client.GetDocumentTypesAsync();

        // Assert
        result.Should().NotBeNull();
        result.Count.Should().Be(4);
    }

    [Fact]
    public async Task CreateDocumentTypeAsync_WhenSuccessful_ReturnsDocumentType()
    {
        // Arrange
        _factory.SetupPost("api/document_types/", TestFixtures.DocumentTypes.CreateDocumentTypeJson(1, "Invoice"));

        // Act
        var result = await _factory.Client.CreateDocumentTypeAsync(new DocumentTypeCreateRequest { Name = "Invoice" });

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("Invoice");
    }

    #endregion

    #region Storage Path Tests

    [Fact]
    public async Task GetStoragePathsAsync_ReturnsStoragePathList()
    {
        // Arrange
        _factory.MockHandler
            .When(HttpMethod.Get, "https://paperless.example.com/api/storage_paths/*")
            .Respond("application/json", TestFixtures.StoragePaths.CreateStoragePathListJson(2));

        // Act
        var result = await _factory.Client.GetStoragePathsAsync();

        // Assert
        result.Should().NotBeNull();
        result.Count.Should().Be(2);
    }

    [Fact]
    public async Task CreateStoragePathAsync_WhenSuccessful_ReturnsStoragePath()
    {
        // Arrange
        _factory.SetupPost("api/storage_paths/", TestFixtures.StoragePaths.CreateStoragePathJson(1, "Archive"));

        // Act
        var result = await _factory.Client.CreateStoragePathAsync(new StoragePathCreateRequest
        {
            Name = "Archive",
            Path = "{correspondent}/{year}"
        });

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("Archive");
    }

    #endregion

    #region Custom Field Tests

    [Theory]
    [InlineData("2.13.5", true)]
    [InlineData("v2.13.5", true)]
    [InlineData("2.14.0", false)]
    [InlineData("2.20.15", false)]
    [InlineData("invalid", false)]
    [InlineData(null, false)]
    public void UsesLegacySelectOptionFormat_DetectsV214Boundary(string? version, bool expected)
    {
        PaperlessClient.UsesLegacySelectOptionFormat(version).Should().Be(expected);
    }

    [Fact]
    public async Task GetCustomFieldsAsync_ReturnsCustomFieldList()
    {
        // Arrange
        _factory.MockHandler
            .When(HttpMethod.Get, "https://paperless.example.com/api/custom_fields/*")
            .Respond("application/json", TestFixtures.CustomFields.CreateCustomFieldListJson(3));

        // Act
        var result = await _factory.Client.GetCustomFieldsAsync();

        // Assert
        result.Should().NotBeNull();
        result.Count.Should().Be(3);
    }

    [Fact]
    public async Task CreateCustomFieldAsync_WhenSuccessful_ReturnsCustomField()
    {
        // Arrange
        _factory.SetupPost("api/custom_fields/", TestFixtures.CustomFields.CreateCustomFieldJson(1, "Invoice Number"));

        // Act
        var result = await _factory.Client.CreateCustomFieldAsync(new CustomFieldCreateRequest
        {
            Name = "Invoice Number",
            DataType = "string"
        });

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("Invoice Number");
    }

    #endregion

    #region Bulk Operations Tests

    [Fact]
    public async Task BulkEditDocumentsAsync_WhenSuccessful_ReturnsSuccessWithoutError()
    {
        // Arrange
        _factory.SetupPost("api/documents/bulk_edit/", "{}");

        // Act
        var result = await _factory.Client.BulkEditDocumentsAsync([1, 2, 3], "add_tag", new { tag = 5 });

        // Assert
        result.Success.Should().BeTrue();
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task BulkEditDocumentsAsync_WhenHttpRequestFails_ReturnsResponseDetails()
    {
        // Arrange
        const string errorBody = "{\"detail\":\"Invalid tag ID\"}";
        _factory.SetupPostWithError("api/documents/bulk_edit/", HttpStatusCode.BadRequest, errorBody);

        // Act
        var result = await _factory.Client.BulkEditDocumentsAsync([1, 2, 3], "add_tag", new { tag = 999 });

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be($"HTTP 400: {errorBody}");
    }

    [Fact]
    public async Task BulkEditDocumentsAsync_WhenTransportFails_ReturnsExceptionMessage()
    {
        // Arrange
        _factory.MockHandler
            .When(HttpMethod.Post, "https://paperless.example.com/api/documents/bulk_edit/")
            .Throw(new HttpRequestException("network unavailable"));

        // Act
        var result = await _factory.Client.BulkEditDocumentsAsync([1], "reprocess");

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("network unavailable");
    }

    [Fact]
    public async Task BulkEditDocumentsAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        _factory.MockHandler
            .When(HttpMethod.Post, "https://paperless.example.com/api/documents/bulk_edit/")
            .Throw(new OperationCanceledException(cancellation.Token));

        // Act
        Func<Task> act = async () =>
            await _factory.Client.BulkEditDocumentsAsync(
                [1],
                "reprocess",
                cancellationToken: cancellation.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task BulkEditObjectsAsync_WhenSuccessful_ReturnsSuccessWithoutError()
    {
        // Arrange
        _factory.SetupPost("api/bulk_edit_objects/", "{}");

        // Act
        var result = await _factory.Client.BulkEditObjectsAsync([1, 2], "tags", "delete");

        // Assert
        result.Success.Should().BeTrue();
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task BulkEditObjectsAsync_WhenHttpRequestFails_ReturnsResponseDetails()
    {
        // Arrange
        const string errorBody = "{\"detail\":\"Object is protected\"}";
        _factory.SetupPostWithError("api/bulk_edit_objects/", HttpStatusCode.Conflict, errorBody);

        // Act
        var result = await _factory.Client.BulkEditObjectsAsync([1, 2], "tags", "delete");

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be($"HTTP 409: {errorBody}");
    }

    [Fact]
    public async Task BulkEditObjectsAsync_WhenTransportFails_ReturnsExceptionMessage()
    {
        // Arrange
        _factory.MockHandler
            .When(HttpMethod.Post, "https://paperless.example.com/api/bulk_edit_objects/")
            .Throw(new HttpRequestException("network unavailable"));

        // Act
        var result = await _factory.Client.BulkEditObjectsAsync([1], "tags", "delete");

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("network unavailable");
    }

    [Fact]
    public async Task BulkEditObjectsAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        _factory.MockHandler
            .When(HttpMethod.Post, "https://paperless.example.com/api/bulk_edit_objects/")
            .Throw(new OperationCanceledException(cancellation.Token));

        // Act
        Func<Task> act = async () =>
            await _factory.Client.BulkEditObjectsAsync(
                [1],
                "tags",
                "delete",
                cancellationToken: cancellation.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region Request Body Serialization Tests

    // Wire-format documentation tests. These capture the outbound request body and
    // assert the expected payload reaches the HTTP layer. They DO NOT reproduce the
    // production bug that motivated the fix — that bug only manifested through the DI
    // HttpClient pipeline (DelegatingHandler + Polly retry policy), and these tests
    // wire MockHttp directly into the HttpClient, bypassing that chain. Treat these as
    // pinning tests for the new code path. A higher-fidelity regression test would
    // need to register PaperlessClient through ServiceCollection with the same handler
    // pipeline used in Program.cs.

    [Fact]
    public async Task CreateCorrespondentAsync_SendsNameInRequestBody()
    {
        // Arrange — capture the actual outbound body
        string? capturedBody = null;
        _factory.MockHandler
            .When(HttpMethod.Post, "https://paperless.example.com/api/correspondents/")
            .With(req =>
            {
                capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return true;
            })
            .Respond("application/json", TestFixtures.Correspondents.CreateCorrespondentJson(1, "ACME Corp"));

        // Act
        await _factory.Client.CreateCorrespondentAsync(new CorrespondentCreateRequest { Name = "ACME Corp" });

        // Assert — the body must include the name on the wire.
        capturedBody.Should().NotBeNullOrEmpty();
        capturedBody.Should().Contain("\"name\":\"ACME Corp\"");
    }

    [Fact]
    public async Task UpdateDocumentAsync_SendsUpdatedFieldsInRequestBody()
    {
        // Arrange
        string? capturedBody = null;
        _factory.MockHandler
            .When(HttpMethod.Patch, "https://paperless.example.com/api/documents/42/")
            .With(req =>
            {
                capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return true;
            })
            .Respond("application/json", "{\"id\":42}");

        // Act
        await _factory.Client.UpdateDocumentAsync(42, new DocumentUpdateRequest
        {
            Title = "New Title",
            Correspondent = 7,
            Tags = new List<int> { 1, 2 }
        });

        // Assert — the PATCH body must contain the updated fields on the wire.
        capturedBody.Should().NotBeNullOrEmpty();
        capturedBody.Should().Contain("\"title\":\"New Title\"");
        capturedBody.Should().Contain("\"correspondent\":7");
        capturedBody.Should().Contain("\"tags\":[1,2]");
    }

    [Fact]
    public async Task BulkEditDocumentsAsync_SerializesInnerParametersWithRuntimeType()
    {
        // Arrange
        string? capturedBody = null;
        _factory.MockHandler
            .When(HttpMethod.Post, "https://paperless.example.com/api/documents/bulk_edit/")
            .With(req =>
            {
                capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return true;
            })
            .Respond("application/json", "{}");

        // Act
        await _factory.Client.BulkEditDocumentsAsync([1, 2, 3], "add_tag", new { tag = 5 });

        // Assert — the inner `parameters` field must contain the actual tag, not `{}`.
        capturedBody.Should().NotBeNullOrEmpty();
        capturedBody.Should().Contain("\"documents\":[1,2,3]");
        capturedBody.Should().Contain("\"method\":\"add_tag\"");
        capturedBody.Should().Contain("\"parameters\":{\"tag\":5}");
    }

    [Fact]
    public async Task BulkEditObjectsAsync_SerializesInnerParametersWithRuntimeType()
    {
        // Arrange
        string? capturedBody = null;
        _factory.MockHandler
            .When(HttpMethod.Post, "https://paperless.example.com/api/bulk_edit_objects/")
            .With(req =>
            {
                capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return true;
            })
            .Respond("application/json", "{}");

        // Act
        await _factory.Client.BulkEditObjectsAsync([10, 20], "tags", "set_permissions",
            new { owner = 3, set_permissions = new { view = new { users = new[] { 1 } } } });

        // Assert
        capturedBody.Should().NotBeNullOrEmpty();
        capturedBody.Should().Contain("\"objects\":[10,20]");
        capturedBody.Should().Contain("\"object_type\":\"tags\"");
        capturedBody.Should().Contain("\"operation\":\"set_permissions\"");
        capturedBody.Should().Contain("\"owner\":3");
    }

    #endregion
}
