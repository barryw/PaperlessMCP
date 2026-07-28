using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using PaperlessMCP.Client;
using PaperlessMCP.Models.Common;
using PaperlessMCP.Models.Documents;
using PaperlessMCP.Utils;
using static PaperlessMCP.Utils.ParsingHelpers;

namespace PaperlessMCP.Tools;

/// <summary>
/// MCP tools for document operations.
/// </summary>
[McpServerToolType]
public static class DocumentTools
{
    [McpServerTool(Name = "paperless_documents_search")]
    [Description("Search for documents with full-text search and filters. Supports pagination.")]
    public static async Task<string> Search(
        PaperlessClient client,
        [Description("Full-text search query")] string? query = null,
        [Description("Filter by tag IDs (comma-separated)")] string? tags = null,
        [Description("Exclude tag IDs (comma-separated)")] string? tagsExclude = null,
        [Description("Filter by correspondent ID")] int? correspondent = null,
        [Description("Filter by document type ID")] int? documentType = null,
        [Description("Filter by storage path ID")] int? storagePath = null,
        [Description("Filter by documents created after this date (YYYY-MM-DD)")] string? createdAfter = null,
        [Description("Filter by documents created before this date (YYYY-MM-DD)")] string? createdBefore = null,
        [Description("Filter by documents added after this date (YYYY-MM-DD)")] string? addedAfter = null,
        [Description("Filter by documents added before this date (YYYY-MM-DD)")] string? addedBefore = null,
        [Description("Filter by archive serial number")] int? archiveSerialNumber = null,
        [Description("Page number (default: 1)")] int page = 1,
        [Description("Page size (default: 25, capped by MAX_PAGE_SIZE)")] int pageSize = 25,
        [Description("Ordering field (e.g., 'created', '-created', 'title')")] string? ordering = null,
        [Description("Include document content in results (default: false). Use paperless_documents_get for full content.")] bool includeContent = false,
        [Description("Max content length per document when includeContent=true (default: 500). Use 0 for unlimited.")] int contentMaxLength = 500)
    {
        var tagIds = ParseIntArray(tags);
        var tagExcludeIds = ParseIntArray(tagsExclude);
        var effectivePageSize = client.GetEffectivePageSize(pageSize);

        DateTime? createdAfterDate = ParseDate(createdAfter);
        DateTime? createdBeforeDate = ParseDate(createdBefore);
        DateTime? addedAfterDate = ParseDate(addedAfter);
        DateTime? addedBeforeDate = ParseDate(addedBefore);

        var searchResult = await client.SearchDocumentsWithResultAsync(
            query: query,
            tags: tagIds,
            tagsExclude: tagExcludeIds,
            correspondent: correspondent,
            documentType: documentType,
            storagePath: storagePath,
            createdAfter: createdAfterDate,
            createdBefore: createdBeforeDate,
            addedAfter: addedAfterDate,
            addedBefore: addedBeforeDate,
            archiveSerialNumber: archiveSerialNumber,
            page: page,
            pageSize: effectivePageSize,
            ordering: ordering
        ).ConfigureAwait(false);

        if (!searchResult.IsSuccess)
        {
            var error = searchResult.Error!;
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                $"Failed to search documents: {error.Message}",
                new { status_code = (int)error.StatusCode },
                new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var result = searchResult.Value!;

        // Map to lightweight summaries to reduce response size
        var summaries = result.Results
            .Select(r => DocumentSummary.FromSearchResult(
                r,
                includeContent,
                contentMaxLength > 0 ? contentMaxLength : null))
            .ToList();

        var response = McpResponse<object>.Success(
            summaries,
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

    [McpServerTool(Name = "paperless_documents_get")]
    [Description("Get a document by its ID.")]
    public static async Task<string> Get(
        PaperlessClient client,
        [Description("Document ID")] int id)
    {
        var document = await client.GetDocumentAsync(id).ConfigureAwait(false);

        if (document == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.NotFound,
                $"Document with ID {id} not found",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<Document>.Success(
            document,
            new McpMeta { PaperlessBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    /// <summary>
    /// Upper bound on the raw file size returnable inline as base64. base64 inflates by ~4/3
    /// and the whole tool response must fit the model's tool-output budget, so this is kept
    /// deliberately small: anything larger must go through paperless_documents_export_to_outbox.
    /// </summary>
    private const int MaxInlineBase64Bytes = 12 * 1024;

    [McpServerTool(Name = "paperless_documents_download")]
    [Description("Get download URLs for a document's original file, preview, and thumbnail. Set returnBase64=true to also inline the file bytes as base64, but only for tiny files: use paperless_documents_export_to_outbox for anything real.")]
    public static async Task<string> Download(
        PaperlessClient client,
        [Description("Document ID")] int id,
        [Description("Also return the file content as base64 (only allowed for tiny files, ~12 KB). Use paperless_documents_export_to_outbox otherwise.")] bool returnBase64 = false)
    {
        var document = await client.GetDocumentAsync(id).ConfigureAwait(false);

        if (document == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.NotFound,
                $"Document with ID {id} not found",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var downloadInfo = client.GetDocumentDownloadInfo(id, document.Title, document.OriginalFileName);

        if (!returnBase64)
        {
            var response = McpResponse<DocumentDownload>.Success(
                downloadInfo,
                new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(response);
        }

        var (file, error) = await client.OpenDocumentFileAsync(id).ConfigureAwait(false);
        if (error != null || file == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                $"Failed to download file for document {id}: {error ?? "empty response"}",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        await using (file)
        {
            // Read one byte past the cap instead of consulting Content-Length: an absent or wrong
            // header must not be able to pull an unbounded body into memory just to reject it.
            byte[] content;
            bool exceeded;
            try
            {
                (content, exceeded) = await file.ReadAtMostAsync(MaxInlineBase64Bytes).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // A body that fails or stalls mid-read is an upstream problem, not a crash: the
                // caller gets an error like any other instead of an exception out of the tool.
                var errorResponse = McpErrorResponse.Create(
                    ErrorCodes.UpstreamError,
                    $"Failed to read file for document {id}: {ex.Message}",
                    meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
                );
                return JsonSerializer.Serialize(errorResponse);
            }
            if (exceeded)
            {
                var size = file.ContentLength is { } length
                    ? $"{length} bytes"
                    : $"larger than {MaxInlineBase64Bytes} bytes";
                var errorResponse = McpErrorResponse.Create(
                    ErrorCodes.Validation,
                    $"File is {size}; too large to inline as base64 (limit {MaxInlineBase64Bytes}). Use paperless_documents_export_to_outbox instead.",
                    meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
                );
                return JsonSerializer.Serialize(errorResponse);
            }

            var okResponse = McpResponse<object>.Success(
                new
                {
                    id,
                    downloadInfo.Title,
                    downloadInfo.OriginalFileName,
                    downloadInfo.DownloadUrl,
                    downloadInfo.PreviewUrl,
                    downloadInfo.ThumbnailUrl,
                    mime_type = file.ContentType,
                    size_bytes = content.Length,
                    content_base64 = Convert.ToBase64String(content)
                },
                new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(okResponse);
        }
    }

    [McpServerTool(Name = "paperless_documents_export_to_outbox")]
    [Description("Download a document's file server-side and write it into the shared outbox directory, returning its path, filename and MIME type. Lets another MCP server (e.g. Gmail) attach the file by path WITHOUT the bytes passing through the model context. Prefer this over base64 for anything but tiny files.")]
    public static async Task<string> ExportToOutbox(
        PaperlessClient client,
        [Description("Document ID")] int id,
        [Description("Override the output filename (optional). By default, the served filename is used, falling back to the archived/original document filename, with the document ID inserted for uniqueness. Only the base file name is used; any directory part is ignored.")] string? filename = null,
        [Description("Export the original uploaded file instead of the archived PDF (optional, default false)")] bool original = false)
    {
        var document = await client.GetDocumentAsync(id).ConfigureAwait(false);

        if (document == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.NotFound,
                $"Document with ID {id} not found",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var (file, error) = await client.OpenDocumentFileAsync(id, original).ConfigureAwait(false);
        if (error != null || file == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                $"Failed to download file for document {id}: {error ?? "empty response"}",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        string safeName;
        string fullPath;
        string? mimeType;
        long written;

        await using (file)
        {
            mimeType = file.ContentType;

            // Name the bytes actually being served. The name the server reports wins; failing
            // that the archived file name, because with original=false Paperless serves the
            // archived PDF and naming that after a .jpg or .docx original mislabels it. Only
            // original=true may fall back to the original file name.
            var derived = original
                ? document.OriginalFileName
                : document.ArchivedFileName ?? AsArchivedName(document.OriginalFileName);

            var rawName = !string.IsNullOrWhiteSpace(filename) ? filename
                : !string.IsNullOrWhiteSpace(file.SuggestedFileName) ? file.SuggestedFileName
                : !string.IsNullOrWhiteSpace(derived) ? derived
                : DefaultExportFileName;

            // Path.GetFileName strips any directory part, so a caller-supplied
            // "../../etc/passwd" collapses to "passwd" and the write stays inside the outbox.
            safeName = Path.GetFileName(rawName!.Trim());
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = DefaultExportFileName;
            }

            // Derived names carry the document id: two documents whose file is called invoice.pdf
            // must not land on the same path, where the second export would replace the first and
            // an earlier result would silently start pointing at the wrong document. An explicit
            // filename is the caller's own choice and is left alone.
            if (string.IsNullOrWhiteSpace(filename))
            {
                safeName = WithDocumentId(safeName, id);
            }

            var outboxDir = Path.GetFullPath(client.OutboxDirectory);
            fullPath = Path.Combine(outboxDir, safeName);
            var tempPath = Path.Combine(outboxDir, $".{safeName}.{Guid.NewGuid():N}.part");

            try
            {
                Directory.CreateDirectory(outboxDir);

                // Stream into a fresh temporary file, then rename it over the destination.
                // FileMode.CreateNew never opens an existing path, so a symlink planted in the
                // shared outbox by another writer cannot be followed, and the rename replaces the
                // link itself rather than truncating whatever it pointed at. It also means a
                // reader on the other side of the volume sees the file whole or not at all.
                var options = new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.Asynchronous
                };
                await using (var destination = new FileStream(tempPath, options))
                {
                    written = await file.CopyToAsync(destination).ConfigureAwait(false);
                }

                File.Move(tempPath, fullPath, overwrite: true);
            }
            catch (Exception ex)
            {
                TryDeleteTempFile(tempPath);
                var errorResponse = McpErrorResponse.Create(
                    ErrorCodes.UpstreamError,
                    $"Failed to write document {id} to outbox: {ex.Message}",
                    meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
                );
                return JsonSerializer.Serialize(errorResponse);
            }
        }

        var okResponse = McpResponse<object>.Success(
            new
            {
                id,
                path = fullPath,
                filename = safeName,
                mime_type = mimeType,
                size_bytes = written
            },
            new McpMeta { PaperlessBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(okResponse);
    }

    /// <summary>Name used for an export when nothing better is known.</summary>
    private const string DefaultExportFileName = "document.pdf";

    /// <summary>
    /// Restates an original file name with the archived version's extension. Paperless archives
    /// to PDF, so a scan uploaded as page.jpg comes back as a PDF when the archived version is
    /// requested, and keeping .jpg would label the export with a type it does not have.
    /// </summary>
    private static string? AsArchivedName(string? originalFileName)
    {
        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            return null;
        }

        var name = Path.GetFileName(originalFileName.Trim());
        return string.IsNullOrWhiteSpace(name) ? null : Path.ChangeExtension(name, ".pdf");
    }

    /// <summary>
    /// Inserts the document id before the extension so derived names are unique per document.
    /// </summary>
    private static string WithDocumentId(string fileName, int id)
    {
        var extension = Path.GetExtension(fileName);
        var stem = fileName[..^extension.Length];

        // A dotfile has no stem by .NET's reckoning (Path.GetExtension(".bashrc") is the whole
        // name), so append instead of producing "_12.bashrc".
        return string.IsNullOrEmpty(stem) ? $"{fileName}_{id}" : $"{stem}_{id}{extension}";
    }

    private static void TryDeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort: the export has already failed and the caller is being told so.
        }
    }

    [McpServerTool(Name = "paperless_documents_preview")]
    [Description("Get the preview URL for a document.")]
    public static async Task<string> Preview(
        PaperlessClient client,
        [Description("Document ID")] int id)
    {
        var document = await client.GetDocumentAsync(id).ConfigureAwait(false);

        if (document == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.NotFound,
                $"Document with ID {id} not found",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var downloadInfo = client.GetDocumentDownloadInfo(id, document.Title, document.OriginalFileName);

        var response = McpResponse<object>.Success(
            new { id, title = document.Title, preview_url = downloadInfo.PreviewUrl },
            new McpMeta { PaperlessBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "paperless_documents_thumbnail")]
    [Description("Get the thumbnail URL for a document.")]
    public static async Task<string> Thumbnail(
        PaperlessClient client,
        [Description("Document ID")] int id)
    {
        var document = await client.GetDocumentAsync(id).ConfigureAwait(false);

        if (document == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.NotFound,
                $"Document with ID {id} not found",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var downloadInfo = client.GetDocumentDownloadInfo(id, document.Title, document.OriginalFileName);

        var response = McpResponse<object>.Success(
            new { id, title = document.Title, thumbnail_url = downloadInfo.ThumbnailUrl },
            new McpMeta { PaperlessBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "paperless_documents_upload")]
    [Description("Upload a new document to Paperless-ngx. Provide file content as base64. For large files, use paperless_documents_upload_from_path instead.")]
    public static async Task<string> Upload(
        PaperlessClient client,
        [Description("Base64-encoded file content")] string fileContent,
        [Description("Original filename with extension")] string fileName,
        [Description("Document title (optional)")] string? title = null,
        [Description("Correspondent ID (optional)")] int? correspondent = null,
        [Description("Document type ID (optional)")] int? documentType = null,
        [Description("Storage path ID (optional)")] int? storagePath = null,
        [Description("Tag IDs (comma-separated, optional)")] string? tags = null,
        [Description("Archive serial number (optional)")] int? archiveSerialNumber = null,
        [Description("Created date (YYYY-MM-DD, optional)")] string? created = null)
    {
        // Reject over-long titles before uploading: Paperless truncates them silently
        // on this path and still reports success. Validate the *effective* title: when
        // title is null or empty, PaperlessClient omits it from the request and Paperless
        // derives the title from the uploaded file's stem, which is truncated just the
        // same. The stem must be computed with pathlib semantics, not
        // Path.GetFileNameWithoutExtension — see TitleValidation.GetFallbackTitle.
        // fileName is what PaperlessClient puts in the multipart part, so Paperless sees
        // exactly this string.
        var effectiveTitle = string.IsNullOrEmpty(title)
            ? TitleValidation.GetFallbackTitle(fileName)
            : title;
        if (!TitleValidation.IsValid(effectiveTitle, out var titleError))
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.Validation,
                titleError!,
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        byte[] fileBytes;
        try
        {
            fileBytes = Convert.FromBase64String(fileContent);
        }
        catch (FormatException)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.Validation,
                "Invalid base64 file content",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var metadata = new DocumentUploadRequest
        {
            Title = title,
            Correspondent = correspondent,
            DocumentType = documentType,
            StoragePath = storagePath,
            Tags = ParseIntArray(tags)?.ToList(),
            ArchiveSerialNumber = archiveSerialNumber,
            Created = ParseDate(created)
        };

        var taskId = await client.UploadDocumentAsync(fileBytes, fileName, metadata).ConfigureAwait(false);

        if (taskId == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                "Failed to upload document",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<object>.Success(
            new { task_id = taskId, status = "queued", message = "Document uploaded and queued for processing" },
            new McpMeta { PaperlessBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "paperless_documents_upload_from_path")]
    [Description("Upload a document from a local file path. More reliable than base64 upload for large files. Includes automatic retries.")]
    public static async Task<string> UploadFromPath(
        PaperlessClient client,
        [Description("Absolute path to the file to upload")] string filePath,
        [Description("Document title (optional, defaults to filename)")] string? title = null,
        [Description("Correspondent ID (optional)")] int? correspondent = null,
        [Description("Document type ID (optional)")] int? documentType = null,
        [Description("Storage path ID (optional)")] int? storagePath = null,
        [Description("Tag IDs (comma-separated, optional)")] string? tags = null,
        [Description("Archive serial number (optional)")] int? archiveSerialNumber = null,
        [Description("Created date (YYYY-MM-DD, optional)")] string? created = null)
    {
        // Expand ~ to home directory
        if (filePath.StartsWith("~/"))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            filePath = Path.Combine(home, filePath[2..]);
        }

        // Validate path
        if (!Path.IsPathRooted(filePath))
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.Validation,
                "File path must be absolute",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        if (!File.Exists(filePath))
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.NotFound,
                $"File not found: {filePath}",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        // Validate the *effective* title: when no title is supplied (null or empty — an
        // empty title would be omitted from the request and Paperless would fall back to
        // the stem server-side anyway) we derive one from the file name, and that derived
        // title is just as vulnerable to silent truncation.
        // PaperlessClient uploads this file as Path.GetFileName(filePath), so that base
        // name is what Paperless applies its own stem rules to; strip the directory the
        // same way here and let GetFallbackTitle handle the extension pathlib-style.
        var effectiveTitle = string.IsNullOrEmpty(title)
            ? TitleValidation.GetFallbackTitle(Path.GetFileName(filePath))
            : title;
        if (!TitleValidation.IsValid(effectiveTitle, out var titleError))
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.Validation,
                titleError!,
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var fileInfo = new FileInfo(filePath);
        var metadata = new DocumentUploadRequest
        {
            Title = effectiveTitle,
            Correspondent = correspondent,
            DocumentType = documentType,
            StoragePath = storagePath,
            Tags = ParseIntArray(tags)?.ToList(),
            ArchiveSerialNumber = archiveSerialNumber,
            Created = ParseDate(created)
        };

        var (taskId, error) = await client.UploadDocumentFromPathAsync(filePath, metadata).ConfigureAwait(false);

        if (taskId == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                error ?? "Failed to upload document",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<object>.Success(
            new
            {
                task_id = taskId,
                status = "queued",
                message = "Document uploaded and queued for processing",
                file_name = fileInfo.Name,
                file_size = fileInfo.Length
            },
            new McpMeta { PaperlessBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "paperless_documents_update")]
    [Description("Update document metadata (title, correspondent, type, tags, etc.).")]
    public static async Task<string> Update(
        PaperlessClient client,
        [Description("Document ID")] int id,
        [Description("New title (optional)")] string? title = null,
        [Description("Correspondent ID (optional, use -1 to clear)")] int? correspondent = null,
        [Description("Document type ID (optional, use -1 to clear)")] int? documentType = null,
        [Description("Storage path ID (optional, use -1 to clear)")] int? storagePath = null,
        [Description("Tag IDs to set (comma-separated, optional)")] string? tags = null,
        [Description("Archive serial number (optional)")] int? archiveSerialNumber = null,
        [Description("Created date (YYYY-MM-DD, optional)")] string? created = null,
        [Description("Include the document's full OCR content in the response (default: false). Leave false for metadata-only updates to save tokens.")] bool includeContent = false)
    {
        // Paperless does reject an over-long title on this path (DRF MaxLengthValidator(128)),
        // but we validate locally anyway: it keeps the contract identical across all three
        // write paths, applies the stricter 127 that upload needs, and saves a round-trip.
        if (!TitleValidation.IsValid(title, out var titleError))
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.Validation,
                titleError!,
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var request = new DocumentUpdateRequest
        {
            Title = title,
            Correspondent = correspondent == -1 ? null : correspondent,
            DocumentType = documentType == -1 ? null : documentType,
            StoragePath = storagePath == -1 ? null : storagePath,
            Tags = ParseIntArray(tags)?.ToList(),
            ArchiveSerialNumber = archiveSerialNumber,
            Created = ParseDate(created)
        };

        var result = await client.UpdateDocumentWithResultAsync(id, request).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            var error = result.Error!;
            var errorResponse = McpErrorResponse.Create(
                error.StatusCode == System.Net.HttpStatusCode.NotFound ? ErrorCodes.NotFound : ErrorCodes.UpstreamError,
                $"Failed to update document {id}: {error.Message}",
                new { status_code = (int)error.StatusCode, response_body = error.ResponseBody },
                new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        // Metadata updates do not need the full OCR content echoed back; stripping it
        // keeps bulk rename/retag workflows from burning tokens on unused content.
        var document = includeContent ? result.Value! : result.Value! with { Content = string.Empty };

        var response = McpResponse<Document>.Success(
            document,
            new McpMeta { PaperlessBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "paperless_documents_delete")]
    [Description("Delete a document. Requires explicit confirmation.")]
    public static async Task<string> Delete(
        PaperlessClient client,
        [Description("Document ID")] int id,
        [Description("Must be true to confirm deletion")] bool confirm = false)
    {
        if (!confirm)
        {
            // Get document info for dry run
            var document = await client.GetDocumentAsync(id).ConfigureAwait(false);

            if (document == null)
            {
                var notFoundResponse = McpErrorResponse.Create(
                    ErrorCodes.NotFound,
                    $"Document with ID {id} not found",
                    meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
                );
                return JsonSerializer.Serialize(notFoundResponse);
            }

            var dryRunResponse = McpErrorResponse.Create(
                ErrorCodes.ConfirmationRequired,
                "Deletion requires confirm=true. This is a dry run showing what would be deleted.",
                new
                {
                    document_id = id,
                    title = document.Title,
                    original_file_name = document.OriginalFileName,
                    created = document.Created
                },
                new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(dryRunResponse);
        }

        var success = await client.DeleteDocumentAsync(id).ConfigureAwait(false);

        if (!success)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                $"Failed to delete document with ID {id}",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<object>.Success(
            new { deleted = true, document_id = id },
            new McpMeta { PaperlessBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "paperless_documents_bulk_update")]
    [Description("Perform bulk operations on multiple documents. Supports dry run mode.")]
    public static async Task<string> BulkUpdate(
        PaperlessClient client,
        [Description("Document IDs (comma-separated)")] string documentIds,
        [Description("Operation: add_tag, remove_tag, set_correspondent, set_document_type, set_storage_path, delete, reprocess")] string operation,
        [Description("Parameter value (e.g., tag ID, correspondent ID)")] int? value = null,
        [Description("Dry run mode - shows what would change without applying")] bool dryRun = true,
        [Description("Must be true to execute the operation")] bool confirm = false)
    {
        var ids = ParseIntArray(documentIds);

        if (ids == null || ids.Length == 0)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.Validation,
                "No valid document IDs provided",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var validOperations = new[] { "add_tag", "remove_tag", "set_correspondent", "set_document_type", "set_storage_path", "delete", "reprocess" };
        if (!validOperations.Contains(operation))
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.Validation,
                $"Invalid operation. Valid operations: {string.Join(", ", validOperations)}",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        if (dryRun || !confirm)
        {
            var dryRunResult = new BulkOperationResult
            {
                AffectedIds = ids,
                Warnings = new List<string>
                {
                    dryRun ? "This is a dry run. Set dry_run=false and confirm=true to execute." : "Set confirm=true to execute the operation."
                },
                Executed = false
            };

            var dryRunResponse = McpResponse<BulkOperationResult>.Success(
                dryRunResult,
                new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(dryRunResponse);
        }

        object? parameters = operation switch
        {
            "add_tag" or "remove_tag" => new { tag = value },
            "set_correspondent" => new { correspondent = value },
            "set_document_type" => new { document_type = value },
            "set_storage_path" => new { storage_path = value },
            _ => null
        };

        var (success, bulkError) = await client.BulkEditDocumentsAsync(ids, operation, parameters).ConfigureAwait(false);

        if (!success)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                $"Bulk operation failed: {bulkError}",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var result = new BulkOperationResult
        {
            AffectedIds = ids,
            Executed = true
        };

        var response = McpResponse<BulkOperationResult>.Success(
            result,
            new McpMeta { PaperlessBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "paperless_documents_reprocess")]
    [Description("Reprocess a document's OCR and content extraction.")]
    public static async Task<string> Reprocess(
        PaperlessClient client,
        [Description("Document ID")] int id,
        [Description("Must be true to confirm reprocessing")] bool confirm = false)
    {
        if (!confirm)
        {
            var document = await client.GetDocumentAsync(id).ConfigureAwait(false);

            if (document == null)
            {
                var notFoundResponse = McpErrorResponse.Create(
                    ErrorCodes.NotFound,
                    $"Document with ID {id} not found",
                    meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
                );
                return JsonSerializer.Serialize(notFoundResponse);
            }

            var dryRunResponse = McpErrorResponse.Create(
                ErrorCodes.ConfirmationRequired,
                "Reprocessing requires confirm=true. This will re-run OCR on the document.",
                new { document_id = id, title = document.Title },
                new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(dryRunResponse);
        }

        var (success, reprocessError) = await client.BulkEditDocumentsAsync(new[] { id }, "reprocess").ConfigureAwait(false);

        if (!success)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                $"Failed to reprocess document with ID {id}: {reprocessError}",
                meta: new McpMeta { PaperlessBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<object>.Success(
            new { document_id = id, status = "queued", message = "Document queued for reprocessing" },
            new McpMeta { PaperlessBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

}
