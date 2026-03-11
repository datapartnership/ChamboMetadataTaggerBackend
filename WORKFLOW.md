# Metadata Tagging Workflow Guide

## Overview

This document describes the complete workflow for managing file metadata tagging using Azure Blob Storage integration.

## Key Features

- **Direct Blob Assignment**: Assign files directly from Azure Blob Storage to taggers without manual import
- **Automatic Import**: Files are automatically imported to the database when assigned
- **Bulk Sync**: Import all blob files at once with the sync endpoint
- **Status Tracking**: Monitor file status (Unassigned, Assigned, InProgress, Completed)

## Admin Operations

### 1. Initial Setup

```bash
# Login as admin
curl -X POST https://localhost:5001/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@metadatatagging.com","password":"Admin123!"}'
```

### 2. Create Tagger Users

```bash
curl -X POST https://localhost:5001/api/admin/users \
  -H "Authorization: Bearer ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "username": "tagger1",
    "email": "tagger1@example.com",
    "password": "Tagger123!",
    "role": "Tagger"
  }'
```

### 3. View Available Files in Blob Storage

```bash
curl -X GET https://localhost:5001/api/admin/blobs \
  -H "Authorization: Bearer ADMIN_TOKEN"
```

**Response:**
```json
{
  "success": true,
  "data": [
    {
      "blobName": "invoice_2024_001.pdf",
      "fileUrl": "https://storage.blob.core.windows.net/files/invoice_2024_001.pdf",
      "fileSize": 1024000,
      "contentType": "application/pdf",
      "lastModified": "2024-01-15T10:30:00Z"
    }
  ]
}
```

### 3a. Preview Any Blob File (Before Assignment)

You can preview any blob file directly without importing or assigning it:

```bash
curl -X GET "https://localhost:5001/api/admin/blobs/invoice_2024_001.pdf/preview?expiryMinutes=60" \
  -H "Authorization: Bearer ADMIN_TOKEN"
```

**Response:**
```json
{
  "success": true,
  "data": {
    "fileId": 0,
    "fileName": "invoice_2024_001.pdf",
    "blobName": "invoice_2024_001.pdf",
    "previewUrl": "https://storage.blob.core.windows.net/files/invoice_2024_001.pdf?sv=2021-12-02&se=2024-01-15T15:30:00Z&sr=b&sp=r&sig=...",
    "expiresAt": "2024-01-15T15:30:00Z",
    "fileSize": 1024000,
    "contentType": "application/pdf"
  }
}
```

This is useful for:
- Quickly checking file contents before deciding to assign
- Verifying file quality before importing
- Reviewing files without cluttering the database

### 4. Option A: Sync All Files (Bulk Import)

```bash
curl -X POST https://localhost:5001/api/admin/sync-blobs \
  -H "Authorization: Bearer ADMIN_TOKEN"
```

**Response:**
```json
{
  "success": true,
  "data": {
    "totalBlobs": 150,
    "importedFiles": 125,
    "existingFiles": 25
  },
  "message": "Synced 125 new files from blob storage"
}
```

### 5. Option B: Assign Directly from Blob Storage (Recommended)

```bash
curl -X POST https://localhost:5001/api/admin/assign-blob-file \
  -H "Authorization: Bearer ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "blobName": "invoice_2024_001.pdf",
    "userId": 2
  }'
```

**What happens:**
1. System checks if file exists in database
2. If not, automatically imports from blob storage
3. Creates assignment to specified tagger
4. Updates file status to "Assigned"

### 6. Monitor Progress

```bash
curl -X GET https://localhost:5001/api/admin/tagging-progress \
  -H "Authorization: Bearer ADMIN_TOKEN"
```

**Response:**
```json
{
  "success": true,
  "data": [
    {
      "userId": 2,
      "username": "tagger1",
      "totalAssigned": 50,
      "totalCompleted": 35,
      "completedFiles": [
        {
          "fileId": 1,
          "fileName": "invoice_2024_001.pdf",
          "completedAt": "2024-01-15T14:30:00Z"
        }
      ]
    }
  ]
}
```

## Tagger Operations

### 1. Login

```bash
curl -X POST https://localhost:5001/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"tagger1@example.com","password":"Tagger123!"}'
```

### 2. View Assigned Files

```bash
curl -X GET https://localhost:5001/api/tagger/my-files \
  -H "Authorization: Bearer TAGGER_TOKEN"
```

**Response:**
```json
{
  "success": true,
  "data": [
    {
      "id": 1,
      "fileName": "invoice_2024_001.pdf",
      "fileUrl": "https://storage.blob.core.windows.net/files/invoice_2024_001.pdf",
      "blobName": "invoice_2024_001.pdf",
      "fileSize": 1024000,
      "contentType": "application/pdf",
      "uploadedAt": "2024-01-15T10:30:00Z",
      "status": "Assigned",
      "tags": [],
      "assignedToUserIds": [2]
    }
  ]
}
```

### 3. Get File Preview URL

```bash
curl -X GET "https://localhost:5001/api/tagger/files/1/preview?expiryMinutes=60" \
  -H "Authorization: Bearer TAGGER_TOKEN"
```

**Response:**
```json
{
  "success": true,
  "data": {
    "fileId": 1,
    "fileName": "invoice_2024_001.pdf",
    "blobName": "invoice_2024_001.pdf",
    "previewUrl": "https://storage.blob.core.windows.net/files/invoice_2024_001.pdf?sv=2021-12-02&se=2024-01-15T15:30:00Z&sr=b&sp=r&sig=...",
    "expiresAt": "2024-01-15T15:30:00Z",
    "fileSize": 1024000,
    "contentType": "application/pdf"
  }
}
```

The `previewUrl` is a secure SAS (Shared Access Signature) URL with temporary read-only access.

### 4. Add Metadata Tags

```bash
curl -X POST https://localhost:5001/api/tagger/files/1/tags \
  -H "Authorization: Bearer TAGGER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "tags": [
      {"tagKey": "document_type", "tagValue": "invoice"},
      {"tagKey": "year", "tagValue": "2024"},
      {"tagKey": "vendor", "tagValue": "Acme Corp"},
      {"tagKey": "amount", "tagValue": "1500.00"},
      {"tagKey": "currency", "tagValue": "USD"},
      {"tagKey": "status", "tagValue": "paid"}
    ]
  }'
```

**File status changes to "InProgress"**

### 5. Complete Tagging

```bash
curl -X POST https://localhost:5001/api/tagger/files/1/complete \
  -H "Authorization: Bearer TAGGER_TOKEN"
```

**File status changes to "Completed"**

## File Status Flow

```
Unassigned → Assigned → InProgress → Completed
```

- **Unassigned**: File in blob storage, not yet assigned
- **Assigned**: File assigned to tagger, no tags added yet
- **InProgress**: Tagger has started adding tags
- **Completed**: Tagger marked file as complete

## Best Practices

### For Admins

1. **Use Direct Blob Assignment**: Prefer `assign-blob-file` over manual import + assign
2. **Bulk Sync for Initial Setup**: Use `sync-blobs` when starting with many existing files
3. **Monitor Progress Regularly**: Check tagging progress to identify bottlenecks
4. **Multiple Assignments**: Same file can be assigned to multiple taggers for verification

### For Taggers

1. **Complete in Batches**: Process files in logical groups
2. **Consistent Tag Keys**: Use standardized tag keys across files
3. **Mark Complete Only When Done**: Only mark files complete when all tags are added
4. **Review Before Completion**: Verify all tags are accurate before marking complete

## Error Handling

### Common Errors

**File not found in blob storage:**
```json
{
  "success": false,
  "message": "Failed to assign blob file. Check if blob exists and user is a Tagger."
}
```

**User is not a Tagger:**
```json
{
  "success": false,
  "message": "Failed to assign file. Check if file and user exist, and user is a Tagger."
}
```

**Already assigned:**
```json
{
  "success": false,
  "message": "Failed to assign file. Check if file and user exist, and user is a Tagger."
}
```

## API Endpoint Summary

### Admin Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/admin/blobs` | List all files in blob storage |
| GET | `/api/admin/blobs/{blobName}/preview` | Preview any blob file (no assignment required) |
| POST | `/api/admin/sync-blobs` | Import all blob files to database |
| POST | `/api/admin/assign-blob-file` | Assign blob file to tagger (auto-imports) |
| GET | `/api/admin/files` | Get all files in database |
| GET | `/api/admin/files/{fileId}/preview` | Preview assigned file |
| GET | `/api/admin/tagging-progress` | View tagging progress |

### Tagger Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/tagger/my-files` | View assigned files |
| GET | `/api/tagger/files/{id}/preview` | Get secure preview URL with SAS token |
| POST | `/api/tagger/files/{id}/tags` | Add tags to file |
| POST | `/api/tagger/files/{id}/complete` | Mark file as complete |

## Database Schema

Files are tracked with the following information:

- **FileMetadata**: Core file information from blob storage
- **FileAssignment**: Assignment tracking (who, when, by whom)
- **FileTag**: Key-value metadata tags
- **Status**: Current state of tagging process

All relationships are maintained automatically by the system.
