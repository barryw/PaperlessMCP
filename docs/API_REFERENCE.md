# PaperlessMCP API Reference

Complete reference for all MCP tools provided by PaperlessMCP.

## Table of Contents

- [Health & Capabilities](#health--capabilities)
- [Document Operations](#document-operations)
- [Tag Operations](#tag-operations)
- [Correspondent Operations](#correspondent-operations)
- [Document Type Operations](#document-type-operations)
- [Storage Path Operations](#storage-path-operations)
- [Custom Field Operations](#custom-field-operations)

---

## Health & Capabilities

### `paperless.ping`
Verify connectivity and authentication with Paperless-ngx.

**Returns:** Connection status and server version

### `paperless.capabilities`
Return supported API endpoints and detected Paperless-ngx version information.

**Returns:** Available endpoints, bulk operations, and server capabilities

---

## Document Operations

### `paperless.documents.search`
Search for documents with full-text search and filters.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `query` | string | No | - | Full-text search query |
| `tags` | string | No | - | Filter by tag IDs (comma-separated) |
| `tagsExclude` | string | No | - | Exclude tag IDs (comma-separated) |
| `correspondent` | int | No | - | Filter by correspondent ID |
| `documentType` | int | No | - | Filter by document type ID |
| `storagePath` | int | No | - | Filter by storage path ID |
| `createdAfter` | string | No | - | Filter by creation date (YYYY-MM-DD) |
| `createdBefore` | string | No | - | Filter by creation date (YYYY-MM-DD) |
| `addedAfter` | string | No | - | Filter by added date (YYYY-MM-DD) |
| `addedBefore` | string | No | - | Filter by added date (YYYY-MM-DD) |
| `archiveSerialNumber` | int | No | - | Filter by archive serial number |
| `page` | int | No | 1 | Page number |
| `pageSize` | int | No | 25 | Results per page (max: 100) |
| `ordering` | string | No | - | Sort field (e.g., 'created', '-created', 'title') |
| `includeContent` | bool | No | false | Include document content in results |
| `contentMaxLength` | int | No | 500 | Max content length when `includeContent=true` |

**Returns:** Paginated list of document summaries

### `paperless.documents.get`
Get a document by its ID.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | int | Yes | Document ID |

**Returns:** Complete document details including content

### `paperless.documents.download`
Get download URLs for a document's original file, preview, and thumbnail.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | int | Yes | Document ID |

**Returns:** Object with `original_url`, `preview_url`, `thumbnail_url`

### `paperless.documents.preview`
Get the preview URL for a document.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | int | Yes | Document ID |

**Returns:** Preview URL

### `paperless.documents.thumbnail`
Get the thumbnail URL for a document.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | int | Yes | Document ID |

**Returns:** Thumbnail URL

### `paperless.documents.upload`
Upload a new document to Paperless-ngx via base64-encoded content.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `fileContent` | string | Yes | - | Base64-encoded file content |
| `fileName` | string | Yes | - | Original filename with extension |
| `title` | string | No | - | Document title |
| `correspondent` | int | No | - | Correspondent ID |
| `documentType` | int | No | - | Document type ID |
| `storagePath` | int | No | - | Storage path ID |
| `tags` | string | No | - | Tag IDs (comma-separated) |
| `archiveSerialNumber` | int | No | - | Archive serial number |
| `created` | string | No | - | Created date (YYYY-MM-DD) |

**Returns:** Task ID and upload status

> **Note:** For large files, use `paperless.documents.upload_from_path` instead.

### `paperless.documents.upload_from_path`
Upload a document from a local file path. More reliable for large files.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `filePath` | string | Yes | - | Absolute path to the file |
| `title` | string | No | filename | Document title |
| `correspondent` | int | No | - | Correspondent ID |
| `documentType` | int | No | - | Document type ID |
| `storagePath` | int | No | - | Storage path ID |
| `tags` | string | No | - | Tag IDs (comma-separated) |
| `archiveSerialNumber` | int | No | - | Archive serial number |
| `created` | string | No | - | Created date (YYYY-MM-DD) |

**Returns:** Task ID, upload status, and file information

**Features:** Supports `~/` expansion, automatic retries, file validation

### `paperless.documents.update`
Update document metadata.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `id` | int | Yes | - | Document ID |
| `title` | string | No | - | New title |
| `correspondent` | int | No | - | Correspondent ID (use -1 to clear) |
| `documentType` | int | No | - | Document type ID (use -1 to clear) |
| `storagePath` | int | No | - | Storage path ID (use -1 to clear) |
| `tags` | string | No | - | Tag IDs to set (comma-separated) |
| `archiveSerialNumber` | int | No | - | Archive serial number |
| `created` | string | No | - | Created date (YYYY-MM-DD) |

**Returns:** Updated document

### `paperless.documents.delete`
Delete a document. Requires explicit confirmation.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `id` | int | Yes | - | Document ID |
| `confirm` | bool | No | false | Must be true to confirm deletion |

**Returns:** Deletion status or dry-run preview

> **Safety:** Without `confirm=true`, returns a dry-run preview showing what would be deleted.

### `paperless.documents.bulk_update`
Perform bulk operations on multiple documents.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `documentIds` | string | Yes | - | Document IDs (comma-separated) |
| `operation` | string | Yes | - | Operation: `add_tag`, `remove_tag`, `set_correspondent`, `set_document_type`, `set_storage_path`, `delete`, `reprocess` |
| `value` | int | No | - | Parameter value (e.g., tag ID, correspondent ID) |
| `dryRun` | bool | No | true | Preview changes without applying |
| `confirm` | bool | No | false | Must be true to execute |

**Returns:** Affected document IDs and operation status

> **Safety:** Defaults to dry-run mode to prevent accidental changes.

### `paperless.documents.reprocess`
Reprocess a document's OCR and content extraction.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `id` | int | Yes | - | Document ID |
| `confirm` | bool | No | false | Must be true to confirm reprocessing |

**Returns:** Processing status

---

## Tag Operations

### `paperless.tags.list`
List all tags with pagination.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `page` | int | No | 1 | Page number |
| `pageSize` | int | No | 25 | Results per page (max: 100) |
| `ordering` | string | No | - | Sort field (e.g., 'name', '-document_count') |

**Returns:** Paginated list of tags

### `paperless.tags.get`
Get a tag by its ID.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | int | Yes | Tag ID |

**Returns:** Tag details

### `paperless.tags.create`
Create a new tag.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `name` | string | Yes | - | Tag name |
| `color` | string | No | - | Hex color (e.g., '#ff0000') |
| `match` | string | No | - | Match pattern for auto-tagging |
| `matchingAlgorithm` | int | No | 0 | Matching algorithm (see below) |
| `isInboxTag` | bool | No | false | Mark as inbox tag |

**Matching Algorithms:**
- `0` - None
- `1` - Any word
- `2` - All words
- `3` - Literal match
- `4` - Regular expression
- `5` - Fuzzy match
- `6` - Auto

**Returns:** Created tag

### `paperless.tags.update`
Update an existing tag.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | int | Yes | Tag ID |
| `name` | string | No | New name |
| `color` | string | No | Hex color |
| `match` | string | No | Match pattern |
| `matchingAlgorithm` | int | No | Matching algorithm |
| `isInboxTag` | bool | No | Inbox tag status |

**Returns:** Updated tag

### `paperless.tags.delete`
Delete a tag. Requires explicit confirmation.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `id` | int | Yes | - | Tag ID |
| `confirm` | bool | No | false | Must be true to confirm deletion |

**Returns:** Deletion status or dry-run preview

### `paperless.tags.bulk_delete`
Delete multiple tags.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `tagIds` | string | Yes | - | Tag IDs (comma-separated) |
| `dryRun` | bool | No | true | Preview changes without applying |
| `confirm` | bool | No | false | Must be true to execute |

**Returns:** Affected tag IDs and operation status

---

## Correspondent Operations

### `paperless.correspondents.list`
List all correspondents with pagination.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `page` | int | No | 1 | Page number |
| `pageSize` | int | No | 25 | Results per page (max: 100) |
| `ordering` | string | No | - | Sort field (e.g., 'name', '-document_count', 'last_correspondence') |

**Returns:** Paginated list of correspondents

### `paperless.correspondents.get`
Get a correspondent by its ID.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | int | Yes | Correspondent ID |

**Returns:** Correspondent details

### `paperless.correspondents.create`
Create a new correspondent.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `name` | string | Yes | - | Correspondent name |
| `match` | string | No | - | Match pattern for auto-assignment |
| `matchingAlgorithm` | int | No | 0 | Matching algorithm |

**Returns:** Created correspondent

### `paperless.correspondents.update`
Update an existing correspondent.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | int | Yes | Correspondent ID |
| `name` | string | No | New name |
| `match` | string | No | Match pattern |
| `matchingAlgorithm` | int | No | Matching algorithm |

**Returns:** Updated correspondent

### `paperless.correspondents.delete`
Delete a correspondent. Requires explicit confirmation.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `id` | int | Yes | - | Correspondent ID |
| `confirm` | bool | No | false | Must be true to confirm deletion |

**Returns:** Deletion status or dry-run preview

### `paperless.correspondents.bulk_delete`
Delete multiple correspondents.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `correspondentIds` | string | Yes | - | Correspondent IDs (comma-separated) |
| `dryRun` | bool | No | true | Preview changes without applying |
| `confirm` | bool | No | false | Must be true to execute |

**Returns:** Affected correspondent IDs and operation status

---

## Document Type Operations

### `paperless.document_types.list`
List all document types with pagination.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `page` | int | No | 1 | Page number |
| `pageSize` | int | No | 25 | Results per page (max: 100) |
| `ordering` | string | No | - | Sort field (e.g., 'name', '-document_count') |

**Returns:** Paginated list of document types

### `paperless.document_types.get`
Get a document type by its ID.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | int | Yes | Document type ID |

**Returns:** Document type details

### `paperless.document_types.create`
Create a new document type.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `name` | string | Yes | - | Document type name |
| `match` | string | No | - | Match pattern for auto-assignment |
| `matchingAlgorithm` | int | No | 0 | Matching algorithm |

**Returns:** Created document type

### `paperless.document_types.update`
Update an existing document type.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | int | Yes | Document type ID |
| `name` | string | No | New name |
| `match` | string | No | Match pattern |
| `matchingAlgorithm` | int | No | Matching algorithm |

**Returns:** Updated document type

### `paperless.document_types.delete`
Delete a document type. Requires explicit confirmation.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `id` | int | Yes | - | Document type ID |
| `confirm` | bool | No | false | Must be true to confirm deletion |

**Returns:** Deletion status or dry-run preview

### `paperless.document_types.bulk_delete`
Delete multiple document types.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `documentTypeIds` | string | Yes | - | Document type IDs (comma-separated) |
| `dryRun` | bool | No | true | Preview changes without applying |
| `confirm` | bool | No | false | Must be true to execute |

**Returns:** Affected document type IDs and operation status

---

## Storage Path Operations

### `paperless.storage_paths.list`
List all storage paths with pagination.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `page` | int | No | 1 | Page number |
| `pageSize` | int | No | 25 | Results per page (max: 100) |
| `ordering` | string | No | - | Sort field (e.g., 'name', '-document_count') |

**Returns:** Paginated list of storage paths

### `paperless.storage_paths.get`
Get a storage path by its ID.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | int | Yes | Storage path ID |

**Returns:** Storage path details

### `paperless.storage_paths.create`
Create a new storage path.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `name` | string | Yes | - | Storage path name |
| `path` | string | Yes | - | Path template (e.g., `{correspondent}/{document_type}`) |
| `match` | string | No | - | Match pattern for auto-assignment |
| `matchingAlgorithm` | int | No | 0 | Matching algorithm |

**Returns:** Created storage path

### `paperless.storage_paths.update`
Update an existing storage path.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | int | Yes | Storage path ID |
| `name` | string | No | New name |
| `path` | string | No | Path template |
| `match` | string | No | Match pattern |
| `matchingAlgorithm` | int | No | Matching algorithm |

**Returns:** Updated storage path

### `paperless.storage_paths.delete`
Delete a storage path. Requires explicit confirmation.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `id` | int | Yes | - | Storage path ID |
| `confirm` | bool | No | false | Must be true to confirm deletion |

**Returns:** Deletion status or dry-run preview

### `paperless.storage_paths.bulk_delete`
Delete multiple storage paths.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `storagePathIds` | string | Yes | - | Storage path IDs (comma-separated) |
| `dryRun` | bool | No | true | Preview changes without applying |
| `confirm` | bool | No | false | Must be true to execute |

**Returns:** Affected storage path IDs and operation status

---

## Custom Field Operations

### `paperless.custom_fields.list`
List all custom field definitions with pagination.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `page` | int | No | 1 | Page number |
| `pageSize` | int | No | 25 | Results per page (max: 100) |

**Returns:** Paginated list of custom field definitions

### `paperless.custom_fields.get`
Get a custom field definition by its ID.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | int | Yes | Custom field ID |

**Returns:** Custom field details

### `paperless.custom_fields.create`
Create a new custom field definition.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `name` | string | Yes | - | Custom field name |
| `dataType` | string | Yes | - | Data type (see below) |
| `selectOptions` | string | No | - | Select options (comma-separated, for 'select' type) |
| `defaultCurrency` | string | No | - | Default currency (for 'monetary' type) |

**Data Types:**
- `string` - Text value
- `url` - URL/link
- `date` - Date value
- `boolean` - True/false
- `integer` - Whole number
- `float` - Decimal number
- `monetary` - Currency amount
- `documentlink` - Link to another document
- `select` - Dropdown selection

**Returns:** Created custom field

### `paperless.custom_fields.update`
Update an existing custom field definition.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | int | Yes | Custom field ID |
| `name` | string | No | New name |
| `selectOptions` | string | No | Select options (comma-separated) |
| `defaultCurrency` | string | No | Default currency |

**Returns:** Updated custom field

### `paperless.custom_fields.delete`
Delete a custom field definition. Requires explicit confirmation.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `id` | int | Yes | - | Custom field ID |
| `confirm` | bool | No | false | Must be true to confirm deletion |

**Returns:** Deletion status or dry-run preview

### `paperless.custom_fields.assign`
Assign a custom field value to a document.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `documentId` | int | Yes | Document ID |
| `fieldId` | int | Yes | Custom field ID |
| `value` | string | Yes | Value to assign (type depends on field; for `documentlink`, a comma-separated list of document IDs, e.g. `3,7,12`) |

**Returns:** Assignment status and assigned value
