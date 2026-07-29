using System.Text.Json;
using PaperlessMCP.Models.Common;
using PaperlessMCP.Models.Correspondents;
using PaperlessMCP.Models.CustomFields;
using PaperlessMCP.Models.Documents;
using PaperlessMCP.Models.DocumentTypes;
using PaperlessMCP.Models.StoragePaths;
using PaperlessMCP.Models.Tags;

namespace PaperlessMCP.Tests.Fixtures;

public static class TestFixtures
{
    public static class Documents
    {
        public static Document CreateDocument(int id = 1, string title = "Test Document") => new()
        {
            Id = id,
            Title = title,
            Content = "This is test content for the document.",
            Correspondent = 1,
            DocumentType = 1,
            StoragePath = 1,
            Tags = [1, 2],
            Created = DateTime.UtcNow.AddDays(-10),
            Modified = DateTime.UtcNow,
            Added = DateTime.UtcNow.AddDays(-5),
            ArchiveSerialNumber = 1001,
            OriginalFileName = "test_document.pdf",
            ArchivedFileName = "test_document_archived.pdf",
            Owner = 1,
            CustomFields = []
        };

        public static DocumentSearchResult CreateSearchResult(int id = 1, string title = "Test Document", double score = 1.5, string? content = null) => new()
        {
            Id = id,
            Title = title,
            Content = content ?? "This is test content for the document.",
            Correspondent = 1,
            DocumentType = 1,
            Tags = [1, 2],
            Created = DateTime.UtcNow.AddDays(-10),
            SearchHit = new SearchHit
            {
                Score = score,
                Highlights = $"<span>test</span> content",
                Rank = 1
            }
        };

        public static PaginatedResult<DocumentSearchResult> CreateSearchResults(int count = 3, string? content = null) => new()
        {
            Count = count,
            Next = count > 25 ? "http://example.com/api/documents/?page=2" : null,
            Previous = null,
            Results = Enumerable.Range(1, Math.Min(count, 25))
                .Select(i => CreateSearchResult(i, $"Document {i}", content: content))
                .ToList()
        };

        public static string CreateSearchResultsJson(int count = 3, string? content = null) =>
            JsonSerializer.Serialize(CreateSearchResults(count, content));

        /// <summary>
        /// Creates a long content string for testing truncation behavior.
        /// </summary>
        public static string CreateLongContent(int length = 1000) =>
            string.Join(" ", Enumerable.Repeat("Lorem ipsum dolor sit amet, consectetur adipiscing elit.", length / 56 + 1))[..length];

        public static string CreateDocumentJson(int id = 1, string title = "Test Document") =>
            JsonSerializer.Serialize(CreateDocument(id, title));
    }

    public static class Tags
    {
        public static Tag CreateTag(int id = 1, string name = "Test Tag", int? parent = null) => new()
        {
            Id = id,
            Slug = name.ToLower().Replace(" ", "-"),
            Name = name,
            Color = "#ff0000",
            TextColor = "#ffffff",
            Match = "",
            MatchingAlgorithm = 0,
            IsInboxTag = false,
            DocumentCount = 5,
            Owner = 1,
            Parent = parent
        };

        public static PaginatedResult<Tag> CreateTagList(int count = 3) => new()
        {
            Count = count,
            Next = null,
            Previous = null,
            Results = Enumerable.Range(1, count)
                .Select(i => CreateTag(i, $"Tag {i}"))
                .ToList()
        };

        public static string CreateTagJson(int id = 1, string name = "Test Tag", int? parent = null) =>
            JsonSerializer.Serialize(CreateTag(id, name, parent));

        public static string CreateTagListJson(int count = 3) =>
            JsonSerializer.Serialize(CreateTagList(count));
    }

    public static class Correspondents
    {
        public static Correspondent CreateCorrespondent(int id = 1, string name = "Test Correspondent") => new()
        {
            Id = id,
            Slug = name.ToLower().Replace(" ", "-"),
            Name = name,
            Match = "",
            MatchingAlgorithm = 0,
            DocumentCount = 10,
            LastCorrespondence = DateTime.UtcNow.AddDays(-1),
            Owner = 1
        };

        public static PaginatedResult<Correspondent> CreateCorrespondentList(int count = 3) => new()
        {
            Count = count,
            Next = null,
            Previous = null,
            Results = Enumerable.Range(1, count)
                .Select(i => CreateCorrespondent(i, $"Correspondent {i}"))
                .ToList()
        };

        public static string CreateCorrespondentJson(int id = 1, string name = "Test Correspondent") =>
            JsonSerializer.Serialize(CreateCorrespondent(id, name));

        public static string CreateCorrespondentListJson(int count = 3) =>
            JsonSerializer.Serialize(CreateCorrespondentList(count));
    }

    public static class DocumentTypes
    {
        public static DocumentType CreateDocumentType(int id = 1, string name = "Test Type") => new()
        {
            Id = id,
            Slug = name.ToLower().Replace(" ", "-"),
            Name = name,
            Match = "",
            MatchingAlgorithm = 0,
            DocumentCount = 15,
            Owner = 1
        };

        public static PaginatedResult<DocumentType> CreateDocumentTypeList(int count = 3) => new()
        {
            Count = count,
            Next = null,
            Previous = null,
            Results = Enumerable.Range(1, count)
                .Select(i => CreateDocumentType(i, $"Type {i}"))
                .ToList()
        };

        public static string CreateDocumentTypeJson(int id = 1, string name = "Test Type") =>
            JsonSerializer.Serialize(CreateDocumentType(id, name));

        public static string CreateDocumentTypeListJson(int count = 3) =>
            JsonSerializer.Serialize(CreateDocumentTypeList(count));
    }

    public static class StoragePaths
    {
        public static StoragePath CreateStoragePath(int id = 1, string name = "Test Path") => new()
        {
            Id = id,
            Slug = name.ToLower().Replace(" ", "-"),
            Name = name,
            Path = "{correspondent}/{document_type}",
            Match = "",
            MatchingAlgorithm = 0,
            DocumentCount = 8,
            Owner = 1
        };

        public static PaginatedResult<StoragePath> CreateStoragePathList(int count = 3) => new()
        {
            Count = count,
            Next = null,
            Previous = null,
            Results = Enumerable.Range(1, count)
                .Select(i => CreateStoragePath(i, $"Path {i}"))
                .ToList()
        };

        public static string CreateStoragePathJson(int id = 1, string name = "Test Path") =>
            JsonSerializer.Serialize(CreateStoragePath(id, name));

        public static string CreateStoragePathListJson(int count = 3) =>
            JsonSerializer.Serialize(CreateStoragePathList(count));
    }

    public static class CustomFields
    {
        public static CustomField CreateCustomField(int id = 1, string name = "Test Field", string dataType = "string") => new()
        {
            Id = id,
            Name = name,
            DataType = dataType,
            ExtraData = dataType == "select"
                ? new CustomFieldExtraData
                {
                    SelectOptions =
                    [
                        new SelectOption { Id = "option-1", Label = "Option 1" },
                        new SelectOption { Id = "option-2", Label = "Option 2" }
                    ]
                }
                : null
        };

        public static PaginatedResult<CustomField> CreateCustomFieldList(int count = 3) => new()
        {
            Count = count,
            Next = null,
            Previous = null,
            Results = Enumerable.Range(1, count)
                .Select(i => CreateCustomField(i, $"Field {i}"))
                .ToList()
        };

        public static string CreateCustomFieldJson(int id = 1, string name = "Test Field", string dataType = "string") =>
            JsonSerializer.Serialize(CreateCustomField(id, name, dataType));

        public static string CreateCustomFieldListJson(int count = 3) =>
            JsonSerializer.Serialize(CreateCustomFieldList(count));
    }
}
