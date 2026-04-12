using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaperlessMCP.Configuration;
using PaperlessMCP.Models.Common;
using PaperlessMCP.Models.Correspondents;
using PaperlessMCP.Models.CustomFields;
using PaperlessMCP.Models.Documents;
using PaperlessMCP.Models.DocumentTypes;
using PaperlessMCP.Models.StoragePaths;
using PaperlessMCP.Models.Tags;

namespace PaperlessMCP.Client;

/// <summary>
/// Central client for all Paperless-ngx API operations.
/// </summary>
public class PaperlessClient
{
    private readonly HttpClient _httpClient;
    private readonly PaperlessOptions _options;
    private readonly ILogger<PaperlessClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public PaperlessClient(HttpClient httpClient, IOptions<PaperlessOptions> options, ILogger<PaperlessClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public string BaseUrl => _options.BaseUrl;

    #region Health & Status

    /// <summary>
    /// Checks connectivity and returns API status information.
    /// </summary>
    public async Task<(bool Success, string? Version, string? Error)> PingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("api/status/", cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                // Extract version from the status response
                var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var json = JsonSerializer.Deserialize<JsonElement>(content);
                var version = json.TryGetProperty("pngx_version", out var versionProp)
                    ? versionProp.GetString()
                    : null;

                return (true, version, null);
            }

            return (false, null, $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ping Paperless API");
            return (false, null, ex.Message);
        }
    }

    /// <summary>
    /// Gets status information from the Paperless instance.
    /// </summary>
    public async Task<(bool Success, JsonDocument? Status, string? Error)> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("api/status/", cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken).ConfigureAwait(false);
                return (true, json, null);
            }

            return (false, null, $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Paperless status");
            return (false, null, ex.Message);
        }
    }

    #endregion

    #region Documents

    /// <summary>
    /// Searches for documents with optional filters.
    /// </summary>
    public async Task<PaginatedResult<DocumentSearchResult>> SearchDocumentsAsync(
        string? query = null,
        int[]? tags = null,
        int[]? tagsExclude = null,
        int? correspondent = null,
        int? documentType = null,
        int? storagePath = null,
        DateTime? createdAfter = null,
        DateTime? createdBefore = null,
        DateTime? addedAfter = null,
        DateTime? addedBefore = null,
        int? archiveSerialNumber = null,
        int page = 1,
        int? pageSize = null,
        string? ordering = null,
        CancellationToken cancellationToken = default)
    {
        var queryParams = HttpUtility.ParseQueryString(string.Empty);

        if (!string.IsNullOrEmpty(query))
            queryParams["query"] = query;

        if (tags?.Length > 0)
            foreach (var tag in tags)
                queryParams.Add("tags__id__in", tag.ToString());

        if (tagsExclude?.Length > 0)
            foreach (var tag in tagsExclude)
                queryParams.Add("tags__id__none", tag.ToString());

        if (correspondent.HasValue)
            queryParams["correspondent__id"] = correspondent.Value.ToString();

        if (documentType.HasValue)
            queryParams["document_type__id"] = documentType.Value.ToString();

        if (storagePath.HasValue)
            queryParams["storage_path__id"] = storagePath.Value.ToString();

        if (createdAfter.HasValue)
            queryParams["created__date__gt"] = createdAfter.Value.ToString("yyyy-MM-dd");

        if (createdBefore.HasValue)
            queryParams["created__date__lt"] = createdBefore.Value.ToString("yyyy-MM-dd");

        if (addedAfter.HasValue)
            queryParams["added__date__gt"] = addedAfter.Value.ToString("yyyy-MM-dd");

        if (addedBefore.HasValue)
            queryParams["added__date__lt"] = addedBefore.Value.ToString("yyyy-MM-dd");

        if (archiveSerialNumber.HasValue)
            queryParams["archive_serial_number"] = archiveSerialNumber.Value.ToString();

        queryParams["page"] = page.ToString();
        queryParams["page_size"] = (pageSize ?? _options.MaxPageSize).ToString();

        if (!string.IsNullOrEmpty(ordering))
            queryParams["ordering"] = ordering;

        var url = $"api/documents/?{queryParams}";
        return await GetAsync<PaginatedResult<DocumentSearchResult>>(url, cancellationToken).ConfigureAwait(false)
               ?? new PaginatedResult<DocumentSearchResult>();
    }

    /// <summary>
    /// Gets a document by ID.
    /// </summary>
    public async Task<Document?> GetDocumentAsync(int id, CancellationToken cancellationToken = default)
    {
        return await GetAsync<Document>($"api/documents/{id}/", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates a document.
    /// </summary>
    public async Task<Document?> UpdateDocumentAsync(int id, DocumentUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var result = await UpdateDocumentWithResultAsync(id, request, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? result.Value : null;
    }

    /// <summary>
    /// Updates a document with full error details.
    /// </summary>
    public async Task<ApiResult<Document>> UpdateDocumentWithResultAsync(int id, DocumentUpdateRequest request, CancellationToken cancellationToken = default)
    {
        return await PatchWithResultAsync<Document>($"api/documents/{id}/", request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a document.
    /// </summary>
    public async Task<bool> DeleteDocumentAsync(int id, CancellationToken cancellationToken = default)
    {
        var result = await DeleteDocumentWithResultAsync(id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess;
    }

    /// <summary>
    /// Deletes a document with full error details.
    /// </summary>
    public async Task<ApiResult<bool>> DeleteDocumentWithResultAsync(int id, CancellationToken cancellationToken = default)
    {
        return await DeleteWithResultAsync($"api/documents/{id}/", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Uploads a new document from byte array.
    /// </summary>
    public async Task<string?> UploadDocumentAsync(
        byte[] fileContent,
        string fileName,
        DocumentUploadRequest? metadata = null,
        CancellationToken cancellationToken = default)
    {
        return await UploadDocumentInternalAsync(
            () => new ByteArrayContent(fileContent),
            fileName,
            metadata,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Uploads a new document from a file path. More reliable for large files.
    /// </summary>
    public async Task<(string? TaskId, string? Error)> UploadDocumentFromPathAsync(
        string filePath,
        DocumentUploadRequest? metadata = null,
        int maxRetries = 3,
        CancellationToken cancellationToken = default)
    {
        // Validate file exists
        if (!File.Exists(filePath))
        {
            return (null, $"File not found: {filePath}");
        }

        var fileName = Path.GetFileName(filePath);
        var fileInfo = new FileInfo(filePath);

        _logger.LogInformation("Starting upload of {FileName} ({Size:N0} bytes)", fileName, fileInfo.Length);

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Use StreamContent for efficient memory usage with large files
                await using var fileStream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 81920, // 80KB buffer
                    useAsync: true);

                var streamContent = new StreamContent(fileStream);

                var taskId = await UploadDocumentInternalAsync(
                    () => streamContent,
                    fileName,
                    metadata,
                    cancellationToken,
                    disposeContent: false).ConfigureAwait(false); // StreamContent owns the stream

                if (taskId != null)
                {
                    _logger.LogInformation("Successfully uploaded {FileName}, task ID: {TaskId}", fileName, taskId);
                    return (taskId, null);
                }

                _logger.LogWarning("Upload attempt {Attempt}/{MaxRetries} failed for {FileName}",
                    attempt, maxRetries, fileName);

                if (attempt < maxRetries)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // Exponential backoff
                    _logger.LogInformation("Retrying in {Delay}...", delay);
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (IOException ex) when (attempt < maxRetries)
            {
                _logger.LogWarning(ex, "IO error on attempt {Attempt}/{MaxRetries}, retrying...", attempt, maxRetries);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (attempt < maxRetries)
            {
                _logger.LogWarning(ex, "HTTP error on attempt {Attempt}/{MaxRetries}, retrying...", attempt, maxRetries);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error uploading {FileName}", fileName);
                return (null, $"Upload failed: {ex.Message}");
            }
        }

        return (null, $"Upload failed after {maxRetries} attempts");
    }

    private async Task<string?> UploadDocumentInternalAsync(
        Func<HttpContent> contentFactory,
        string fileName,
        DocumentUploadRequest? metadata,
        CancellationToken cancellationToken,
        bool disposeContent = true)
    {
        using var formContent = new MultipartFormDataContent();
        var fileContent = contentFactory();
        var addedToForm = false;

        try
        {
            formContent.Add(fileContent, "document", fileName);
            addedToForm = true;

            if (metadata != null)
            {
                if (!string.IsNullOrEmpty(metadata.Title))
                    formContent.Add(new StringContent(metadata.Title), "title");

                if (metadata.Correspondent.HasValue)
                    formContent.Add(new StringContent(metadata.Correspondent.Value.ToString()), "correspondent");

                if (metadata.DocumentType.HasValue)
                    formContent.Add(new StringContent(metadata.DocumentType.Value.ToString()), "document_type");

                if (metadata.StoragePath.HasValue)
                    formContent.Add(new StringContent(metadata.StoragePath.Value.ToString()), "storage_path");

                if (metadata.Tags?.Count > 0)
                    foreach (var tag in metadata.Tags)
                        formContent.Add(new StringContent(tag.ToString()), "tags");

                if (metadata.ArchiveSerialNumber.HasValue)
                    formContent.Add(new StringContent(metadata.ArchiveSerialNumber.Value.ToString()), "archive_serial_number");

                if (metadata.Created.HasValue)
                    formContent.Add(new StringContent(metadata.Created.Value.ToString("yyyy-MM-dd")), "created");
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(5)); // 5 minute timeout for uploads

            var response = await _httpClient.PostAsync("api/documents/post_document/", formContent, cts.Token).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                return result.Trim('"'); // Returns task UUID
            }

            var error = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            _logger.LogError("Failed to upload document: {StatusCode} - {Error}", response.StatusCode, error);
            return null;
        }
        finally
        {
            // Only dispose manually if we didn't add it to formContent
            // (formContent owns and will dispose content added to it)
            if (!addedToForm && disposeContent && fileContent is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    /// <summary>
    /// Gets document download URLs.
    /// </summary>
    public DocumentDownload GetDocumentDownloadInfo(int id, string title, string? originalFileName)
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        return new DocumentDownload
        {
            Id = id,
            Title = title,
            OriginalFileName = originalFileName,
            DownloadUrl = $"{baseUrl}/api/documents/{id}/download/",
            PreviewUrl = $"{baseUrl}/api/documents/{id}/preview/",
            ThumbnailUrl = $"{baseUrl}/api/documents/{id}/thumb/"
        };
    }

    /// <summary>
    /// Performs bulk edit operations on documents.
    /// </summary>
    public async Task<(bool Success, string? Error)> BulkEditDocumentsAsync(
        int[] documentIds,
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var request = new BulkEditRequest
        {
            Documents = documentIds,
            Method = method,
            Parameters = parameters
        };

        try
        {
            // Use manual serialization and StringContent to be as explicit as possible (mimic curl)
            var json = JsonSerializer.Serialize(request, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var requestUri = new Uri(_httpClient.BaseAddress!, "api/documents/bulk_edit/");
            _logger.LogDebug("POST {Url} body: {Json}", requestUri, json);

            var response = await _httpClient.PostAsync("api/documents/bulk_edit/", content, cancellationToken).ConfigureAwait(false);
            
            if (response.IsSuccessStatusCode)
            {
                return (true, null);
            }

            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogError("Bulk edit failed: {StatusCode} {Reason} - {Body} (Full Request: {Method} {Url})", 
                response.StatusCode, response.ReasonPhrase, errorBody, response.RequestMessage?.Method, response.RequestMessage?.RequestUri);
            return (false, $"HTTP {response.StatusCode}: {errorBody}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform bulk edit");
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Gets the next available archive serial number.
    /// </summary>
    public async Task<int?> GetNextAsnAsync(CancellationToken cancellationToken = default)
    {
        return await GetAsync<int?>("api/documents/next_asn/", cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Tags

    public async Task<PaginatedResult<Tag>> GetTagsAsync(int page = 1, int? pageSize = null, string? ordering = null, CancellationToken cancellationToken = default)
    {
        var queryParams = HttpUtility.ParseQueryString(string.Empty);
        queryParams["page"] = page.ToString();
        queryParams["page_size"] = (pageSize ?? _options.MaxPageSize).ToString();
        if (!string.IsNullOrEmpty(ordering))
            queryParams["ordering"] = ordering;

        return await GetAsync<PaginatedResult<Tag>>($"api/tags/?{queryParams}", cancellationToken).ConfigureAwait(false)
               ?? new PaginatedResult<Tag>();
    }

    public async Task<Tag?> GetTagAsync(int id, CancellationToken cancellationToken = default)
    {
        return await GetAsync<Tag>($"api/tags/{id}/", cancellationToken).ConfigureAwait(false);
    }

    public async Task<Tag?> CreateTagAsync(TagCreateRequest request, CancellationToken cancellationToken = default)
    {
        var result = await CreateTagWithResultAsync(request, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? result.Value : null;
    }

    public async Task<ApiResult<Tag>> CreateTagWithResultAsync(TagCreateRequest request, CancellationToken cancellationToken = default)
    {
        return await PostWithResultAsync<Tag>("api/tags/", request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Tag?> UpdateTagAsync(int id, TagUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var result = await UpdateTagWithResultAsync(id, request, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? result.Value : null;
    }

    public async Task<ApiResult<Tag>> UpdateTagWithResultAsync(int id, TagUpdateRequest request, CancellationToken cancellationToken = default)
    {
        return await PatchWithResultAsync<Tag>($"api/tags/{id}/", request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteTagAsync(int id, CancellationToken cancellationToken = default)
    {
        var result = await DeleteTagWithResultAsync(id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess;
    }

    public async Task<ApiResult<bool>> DeleteTagWithResultAsync(int id, CancellationToken cancellationToken = default)
    {
        return await DeleteWithResultAsync($"api/tags/{id}/", cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Correspondents

    public async Task<PaginatedResult<Correspondent>> GetCorrespondentsAsync(int page = 1, int? pageSize = null, string? ordering = null, CancellationToken cancellationToken = default)
    {
        var queryParams = HttpUtility.ParseQueryString(string.Empty);
        queryParams["page"] = page.ToString();
        queryParams["page_size"] = (pageSize ?? _options.MaxPageSize).ToString();
        if (!string.IsNullOrEmpty(ordering))
            queryParams["ordering"] = ordering;

        return await GetAsync<PaginatedResult<Correspondent>>($"api/correspondents/?{queryParams}", cancellationToken).ConfigureAwait(false)
               ?? new PaginatedResult<Correspondent>();
    }

    public async Task<Correspondent?> GetCorrespondentAsync(int id, CancellationToken cancellationToken = default)
    {
        return await GetAsync<Correspondent>($"api/correspondents/{id}/", cancellationToken).ConfigureAwait(false);
    }

    public async Task<Correspondent?> CreateCorrespondentAsync(CorrespondentCreateRequest request, CancellationToken cancellationToken = default)
    {
        return await PostAsync<Correspondent>("api/correspondents/", request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Correspondent?> UpdateCorrespondentAsync(int id, CorrespondentUpdateRequest request, CancellationToken cancellationToken = default)
    {
        return await PatchAsync<Correspondent>($"api/correspondents/{id}/", request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteCorrespondentAsync(int id, CancellationToken cancellationToken = default)
    {
        return await DeleteAsync($"api/correspondents/{id}/", cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Document Types

    public async Task<PaginatedResult<DocumentType>> GetDocumentTypesAsync(int page = 1, int? pageSize = null, string? ordering = null, CancellationToken cancellationToken = default)
    {
        var queryParams = HttpUtility.ParseQueryString(string.Empty);
        queryParams["page"] = page.ToString();
        queryParams["page_size"] = (pageSize ?? _options.MaxPageSize).ToString();
        if (!string.IsNullOrEmpty(ordering))
            queryParams["ordering"] = ordering;

        return await GetAsync<PaginatedResult<DocumentType>>($"api/document_types/?{queryParams}", cancellationToken).ConfigureAwait(false)
               ?? new PaginatedResult<DocumentType>();
    }

    public async Task<DocumentType?> GetDocumentTypeAsync(int id, CancellationToken cancellationToken = default)
    {
        return await GetAsync<DocumentType>($"api/document_types/{id}/", cancellationToken).ConfigureAwait(false);
    }

    public async Task<DocumentType?> CreateDocumentTypeAsync(DocumentTypeCreateRequest request, CancellationToken cancellationToken = default)
    {
        return await PostAsync<DocumentType>("api/document_types/", request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DocumentType?> UpdateDocumentTypeAsync(int id, DocumentTypeUpdateRequest request, CancellationToken cancellationToken = default)
    {
        return await PatchAsync<DocumentType>($"api/document_types/{id}/", request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteDocumentTypeAsync(int id, CancellationToken cancellationToken = default)
    {
        return await DeleteAsync($"api/document_types/{id}/", cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Storage Paths

    public async Task<PaginatedResult<StoragePath>> GetStoragePathsAsync(int page = 1, int? pageSize = null, string? ordering = null, CancellationToken cancellationToken = default)
    {
        var queryParams = HttpUtility.ParseQueryString(string.Empty);
        queryParams["page"] = page.ToString();
        queryParams["page_size"] = (pageSize ?? _options.MaxPageSize).ToString();
        if (!string.IsNullOrEmpty(ordering))
            queryParams["ordering"] = ordering;

        return await GetAsync<PaginatedResult<StoragePath>>($"api/storage_paths/?{queryParams}", cancellationToken).ConfigureAwait(false)
               ?? new PaginatedResult<StoragePath>();
    }

    public async Task<StoragePath?> GetStoragePathAsync(int id, CancellationToken cancellationToken = default)
    {
        return await GetAsync<StoragePath>($"api/storage_paths/{id}/", cancellationToken).ConfigureAwait(false);
    }

    public async Task<StoragePath?> CreateStoragePathAsync(StoragePathCreateRequest request, CancellationToken cancellationToken = default)
    {
        return await PostAsync<StoragePath>("api/storage_paths/", request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<StoragePath?> UpdateStoragePathAsync(int id, StoragePathUpdateRequest request, CancellationToken cancellationToken = default)
    {
        return await PatchAsync<StoragePath>($"api/storage_paths/{id}/", request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteStoragePathAsync(int id, CancellationToken cancellationToken = default)
    {
        return await DeleteAsync($"api/storage_paths/{id}/", cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Custom Fields

    public async Task<PaginatedResult<CustomField>> GetCustomFieldsAsync(int page = 1, int? pageSize = null, CancellationToken cancellationToken = default)
    {
        var queryParams = HttpUtility.ParseQueryString(string.Empty);
        queryParams["page"] = page.ToString();
        queryParams["page_size"] = (pageSize ?? _options.MaxPageSize).ToString();

        return await GetAsync<PaginatedResult<CustomField>>($"api/custom_fields/?{queryParams}", cancellationToken).ConfigureAwait(false)
               ?? new PaginatedResult<CustomField>();
    }

    public async Task<CustomField?> GetCustomFieldAsync(int id, CancellationToken cancellationToken = default)
    {
        return await GetAsync<CustomField>($"api/custom_fields/{id}/", cancellationToken).ConfigureAwait(false);
    }

    public async Task<CustomField?> CreateCustomFieldAsync(CustomFieldCreateRequest request, CancellationToken cancellationToken = default)
    {
        return await PostAsync<CustomField>("api/custom_fields/", request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CustomField?> UpdateCustomFieldAsync(int id, CustomFieldUpdateRequest request, CancellationToken cancellationToken = default)
    {
        return await PatchAsync<CustomField>($"api/custom_fields/{id}/", request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteCustomFieldAsync(int id, CancellationToken cancellationToken = default)
    {
        return await DeleteAsync($"api/custom_fields/{id}/", cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Bulk Operations

    /// <summary>
    /// Performs bulk operations on metadata objects (tags, correspondents, etc.).
    /// </summary>
    public async Task<bool> BulkEditObjectsAsync(
        int[] objectIds,
        string objectType,
        string operation,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var request = new
        {
            objects = objectIds,
            object_type = objectType,
            operation,
            parameters
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/bulk_edit_objects/", request, JsonOptions, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform bulk object edit");
            return false;
        }
    }

    #endregion

    #region HTTP Helpers

    private async Task<T?> GetAsync<T>(string url, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false);
            }

            await CreateApiError(response, "GET", url).ConfigureAwait(false);
            return default;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET request failed: {Url}", url);
            return default;
        }
    }

    private async Task<ApiResult<T>> PostWithResultAsync<T>(string url, object request, CancellationToken cancellationToken)
    {
        try
        {
            var body = JsonSerializer.Serialize(request, request.GetType(), JsonOptions);
            _logger.LogDebug("POST {Url} body: {Body}", url, body);
            var content = new StringContent(body, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false);
                return result != null
                    ? ApiResult<T>.Success(result)
                    : ApiResult<T>.Failure(response.StatusCode, "Empty response body");
            }

            var error = await CreateApiError(response, "POST", url).ConfigureAwait(false);
            return ApiResult<T>.Failure(error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "POST request failed: {Url}", url);
            return ApiResult<T>.Failure(HttpStatusCode.InternalServerError, ex.Message);
        }
    }

    private async Task<ApiResult<T>> PatchWithResultAsync<T>(string url, object request, CancellationToken cancellationToken)
    {
        try
        {
            var body = JsonSerializer.Serialize(request, request.GetType(), JsonOptions);
            _logger.LogDebug("PATCH {Url} body: {Body}", url, body);
            var content = new StringContent(body, Encoding.UTF8, "application/json");
            var response = await _httpClient.PatchAsync(url, content, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false);
                return result != null
                    ? ApiResult<T>.Success(result)
                    : ApiResult<T>.Failure(response.StatusCode, "Empty response body");
            }

            var error = await CreateApiError(response, "PATCH", url).ConfigureAwait(false);
            return ApiResult<T>.Failure(error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PATCH request failed: {Url}", url);
            return ApiResult<T>.Failure(HttpStatusCode.InternalServerError, ex.Message);
        }
    }

    private async Task<ApiResult<bool>> DeleteWithResultAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.DeleteAsync(url, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NoContent)
            {
                return ApiResult<bool>.Success(true);
            }

            var error = await CreateApiError(response, "DELETE", url).ConfigureAwait(false);
            return ApiResult<bool>.Failure(error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DELETE request failed: {Url}", url);
            return ApiResult<bool>.Failure(HttpStatusCode.InternalServerError, ex.Message);
        }
    }

    private async Task<ApiError> CreateApiError(HttpResponseMessage response, string method, string url)
    {
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        _logger.LogError("{Method} {Url} failed with {StatusCode}: {Body}",
            method, url, (int)response.StatusCode, body);
        return new ApiError(response.StatusCode, response.ReasonPhrase ?? "Unknown error", body);
    }

    // Legacy methods for backward compatibility - will be removed after migration
    private async Task<T?> PostAsync<T>(string url, object request, CancellationToken cancellationToken)
    {
        var result = await PostWithResultAsync<T>(url, request, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? result.Value : default;
    }

    private async Task<T?> PatchAsync<T>(string url, object request, CancellationToken cancellationToken)
    {
        var result = await PatchWithResultAsync<T>(url, request, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? result.Value : default;
    }

    private async Task<bool> DeleteAsync(string url, CancellationToken cancellationToken)
    {
        var result = await DeleteWithResultAsync(url, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess;
    }

    #endregion
}
