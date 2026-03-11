# Metadata Tagging API

A complete ASP.NET Web API 8.0 backend solution for managing file metadata tagging with role-based access control.

## Features

- **JWT Authentication & Authorization** with three roles:
  - **Admin**: Full access to manage users, files, supervisors, and view tagging progress
  - **Supervisor**: Review and monitor tagging work of assigned students
  - **Tagger** (Student): Access only to assigned files for metadata tagging

- **User Management**: Create, read, update, and delete users
- **Azure Blob Storage Integration**: List and sync files from Azure Blob Storage
- **Automatic File Import**: Files are automatically imported from blob storage when assigned
- **File Assignment System**: Admins can assign blob files directly to taggers
- **Metadata Tagging**: Taggers can add custom key-value tags to files
- **Progress Tracking**: Admins can monitor tagging completion status
- **SQLite Database**: Lightweight database for data persistence

## Workflow

### Admin Workflow
1. Login with admin credentials
2. View all files in Azure Blob Storage (`GET /api/admin/blobs`)
3. Preview any blob file before assignment (`GET /api/admin/blobs/{blobName}/preview`)
4. Create supervisor and tagger users (`POST /api/admin/users`)
5. Assign students to supervisors (`POST /api/admin/assign-student-to-supervisor`)
6. Assign blob files to students (`POST /api/admin/assign-blob-file`)
7. Monitor tagging progress and supervisor assignments

### Tagger Workflow (Student)
1. Login with tagger credentials
2. View assigned files (`GET /api/tagger/my-files`)
3. Get secure preview URL for file (`GET /api/tagger/files/{fileId}/preview`)
4. View/preview the document in the frontend
5. Add metadata tags to files (`POST /api/tagger/files/{fileId}/tags`)
6. Mark files as completed (`POST /api/tagger/files/{fileId}/complete`)

### Supervisor Workflow
1. Login with supervisor credentials
2. View assigned students and their progress (`GET /api/supervisor/my-students`)
3. Review specific student's tagged files (`GET /api/supervisor/students/{studentId}/files`)
4. Preview files with student's tags (`GET /api/supervisor/files/{fileId}/preview`)
5. Mark files as checked/reviewed (`POST /api/supervisor/mark-file-checked`)
6. Add optional notes during review
7. View all students' files with review status (`GET /api/supervisor/all-student-files`)

## Project Structure

```
MetadataTagging/
├── Controllers/
│   ├── AuthController.cs         # Authentication endpoints
│   ├── AdminController.cs        # Admin-only endpoints
│   └── TaggerController.cs       # Tagger-only endpoints
├── Data/
│   └── ApplicationDbContext.cs   # Entity Framework DbContext
├── DTOs/
│   ├── ApiResponse.cs            # Standard API response wrapper
│   ├── AuthDtos.cs               # Authentication DTOs
│   ├── FileDtos.cs               # File-related DTOs
│   └── UserDtos.cs               # User-related DTOs
├── Models/
│   ├── User.cs                   # User entity
│   ├── FileMetadata.cs           # File metadata entity
│   ├── FileAssignment.cs         # File assignment entity
│   ├── FileTag.cs                # File tag entity
│   └── AppSettings.cs            # Configuration options classes
├── Services/
│   ├── IAuthService.cs           # Authentication service interface
│   ├── AuthService.cs            # Authentication implementation
│   ├── IUserService.cs           # User service interface
│   ├── UserService.cs            # User service implementation
│   ├── IFileService.cs           # File service interface
│   ├── FileService.cs            # File service implementation
│   ├── IAzureBlobService.cs      # Azure Blob service interface
│   └── AzureBlobService.cs       # Azure Blob service implementation
├── Program.cs                    # Application entry point
├── appsettings.json              # Configuration file
└── MetadataTagging.csproj        # Project file
```

## Setup Instructions

### Prerequisites

- .NET 8.0 SDK
- Azure Blob Storage account (for file storage)
- Visual Studio 2022 or VS Code

### Configuration

The application supports configuration via environment variables, which override any values in `appsettings.json`. This is the recommended approach for production and containerized deployments.

#### Environment Variables

Copy `.env.example` to `.env` and fill in your values. ASP.NET Core maps environment variables to configuration using double-underscore (`__`) as the section separator:

| Environment Variable | Description | Required |
|---|---|---|
| `DatabaseProvider` | Database backend: `sqlite` (default) or `postgresql` | No (default: `sqlite`) |
| `JwtSettings__SecretKey` | JWT signing secret (min. 32 characters) | **Yes** |
| `JwtSettings__Issuer` | JWT issuer identifier | No (default: `MetadataTaggingAPI`) |
| `JwtSettings__Audience` | JWT audience identifier | No (default: `MetadataTaggingClient`) |
| `JwtSettings__ExpiryMinutes` | Token expiry in minutes | No (default: `480`) |
| `AzureStorage__ConnectionString` | Azure Blob Storage connection string | **Yes** |
| `AzureStorage__ContainerName` | Azure Blob Storage container name | No (default: `files`) |
| `ConnectionStrings__PostgreSQLConnection` | PostgreSQL connection string (required when `DatabaseProvider=postgresql`) | No |
| `DefaultAdmin__Username` | Default admin username | No |
| `DefaultAdmin__Email` | Default admin email | No |
| `DefaultAdmin__Password` | Default admin password | No |
| `DefaultTagger__Username` | Default tagger username | No |
| `DefaultTagger__Email` | Default tagger email | No |
| `DefaultTagger__Password` | Default tagger password | No |
| `DefaultSupervisor__Username` | Default supervisor username | No |
| `DefaultSupervisor__Email` | Default supervisor email | No |
| `DefaultSupervisor__Password` | Default supervisor password | No |

To generate a secure JWT secret key:
```bash
openssl rand -base64 48
```

> **Security note**: Never commit `.env` or `appsettings.Development.json` containing real secrets to source control. The `.env.example` file contains only placeholder values and is safe to commit.

#### Using appsettings.json (local development only)

1. Update `appsettings.json` with your settings:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=metadatatagging.db",
    "PostgreSQLConnection": "Host=localhost;Port=5432;Database=metadatatagging;Username=postgres;Password=yourpassword"
  },
  "DatabaseProvider": "sqlite",
  "JwtSettings": {
    "SecretKey": "your-secret-key-here-min-32-characters-long-change-in-production",
    "Issuer": "MetadataTaggingAPI",
    "Audience": "MetadataTaggingClient",
    "ExpiryMinutes": 480
  },
  "AzureBlobStorage": {
    "ConnectionString": "your-azure-storage-connection-string",
    "ContainerName": "files"
  },
  "DefaultAdmin": {
    "Username": "admin",
    "Email": "admin@metadatatagging.com",
    "Password": "Admin123!"
  }
}
```

2. **Generate a secure JWT secret key** (minimum 32 characters)

3. **Add your Azure Blob Storage connection string**

4. **Configure default admin user** - Update the `DefaultAdmin` section with your preferred admin credentials. This user will be automatically created on first run if it doesn't exist.

### Running the Application

1. Restore dependencies:
```bash
dotnet restore
```

2. Build the project:
```bash
dotnet build
```

3. Run the application:
```bash
dotnet run
```

4. The API will be available at:
   - HTTP: `http://localhost:5000`
   - HTTPS: `https://localhost:5001`
   - Swagger UI: `https://localhost:5001/swagger`

### Database

The SQLite database will be automatically created on first run. A default admin user is automatically created based on the `DefaultAdmin` configuration in `appsettings.json`:

- **Email**: `admin@metadatatagging.com` (configurable)
- **Username**: `admin` (configurable)
- **Password**: `Admin123!` (configurable)
- **Role**: Admin

## API Endpoints

### Authentication

#### POST `/api/auth/login`
Login with email and password
```json
{
  "email": "admin@metadatatagging.com",
  "password": "Admin123!"
}
```

### Admin Endpoints (Requires Admin Role)

#### User Management
- `GET /api/admin/users` - Get all users
- `GET /api/admin/users/{userId}` - Get user by ID
- `POST /api/admin/users` - Create new user
- `PUT /api/admin/users/{userId}` - Update user
- `DELETE /api/admin/users/{userId}` - Delete user (soft delete)
- `GET /api/admin/taggers` - Get all tagger users

#### File Management
- `GET /api/admin/blobs` - List all files from Azure Blob Storage
- `GET /api/admin/blobs/{blobName}/preview` - Get secure preview URL for any blob (no assignment required)
- `POST /api/admin/sync-blobs` - Sync files from blob storage to database
- `GET /api/admin/files` - Get all file metadata
- `GET /api/admin/files/{fileId}` - Get file by ID
- `GET /api/admin/files/{fileId}/preview` - Get secure preview URL for assigned file
- `GET /api/admin/files/unassigned` - Get unassigned files
- `POST /api/admin/files` - Create file metadata manually
- `POST /api/admin/assign-file` - Assign database file to user
- `POST /api/admin/assign-blob-file` - Assign blob file to user (auto-imports if needed)

#### Progress Tracking
- `GET /api/admin/tagging-progress` - Get tagging progress for all users

#### Supervisor Management
- `POST /api/admin/assign-student-to-supervisor` - Assign a student (tagger) to a supervisor
- `POST /api/admin/unassign-student-from-supervisor` - Unassign a student from a supervisor
- `GET /api/admin/supervisor-assignments` - Get all active supervisor-student assignments

### Supervisor Endpoints (Requires Supervisor Role)

- `GET /api/supervisor/my-students` - Get list of assigned students with their progress statistics
- `GET /api/supervisor/students/{studentId}/files` - Get all files assigned to a specific student
- `GET /api/supervisor/all-student-files` - Get all files from all assigned students
- `GET /api/supervisor/files/{fileId}/preview` - Get secure preview URL for student's file
- `POST /api/supervisor/mark-file-checked` - Mark a file as checked/reviewed by supervisor

### Tagger Endpoints (Requires Tagger Role)

- `GET /api/tagger/my-files` - Get files assigned to current user
- `GET /api/tagger/files/{fileId}` - Get specific file details
- `GET /api/tagger/files/{fileId}/preview` - Get secure preview URL for assigned file
- `POST /api/tagger/files/{fileId}/tags` - Add tags to file
- `POST /api/tagger/files/{fileId}/complete` - Mark file tagging as complete

## Usage Examples

### 1. Login as Admin
```bash
curl -X POST https://localhost:5001/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@metadatatagging.com","password":"Admin123!"}'
```

### 2. Create a Tagger User
```bash
curl -X POST https://localhost:5001/api/admin/users \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "username": "tagger1",
    "email": "tagger1@example.com",
    "password": "Tagger123!",
    "role": "Tagger"
  }'
```

### 3. List Blobs from Azure Storage
```bash
curl -X GET https://localhost:5001/api/admin/blobs \
  -H "Authorization: Bearer YOUR_TOKEN"
```

### 3a. Preview Any Blob File (Before Assignment)
```bash
curl -X GET "https://localhost:5001/api/admin/blobs/document.pdf/preview?expiryMinutes=60" \
  -H "Authorization: Bearer YOUR_TOKEN"
```

**Response:**
```json
{
  "success": true,
  "data": {
    "fileId": 0,
    "fileName": "document.pdf",
    "blobName": "document.pdf",
    "previewUrl": "https://storage.blob.core.windows.net/files/document.pdf?sv=2021-12-02&se=...",
    "expiresAt": "2024-01-15T15:30:00Z",
    "fileSize": 1024000,
    "contentType": "application/pdf"
  }
}
```

This allows you to preview any file in blob storage without needing to import or assign it first.

### 4. Sync Files from Blob Storage
```bash
curl -X POST https://localhost:5001/api/admin/sync-blobs \
  -H "Authorization: Bearer YOUR_TOKEN"
```

This will import all files from Azure Blob Storage into the database for tracking and assignment.

### 5. Assign Blob File to Tagger (Recommended Method)
```bash
curl -X POST https://localhost:5001/api/admin/assign-blob-file \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "blobName": "document.pdf",
    "userId": 2
  }'
```

This will automatically import the file from blob storage if it doesn't exist in the database, then assign it to the tagger.

### 6. Alternative: Assign Existing Database File
```bash
curl -X POST https://localhost:5001/api/admin/assign-file \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "fileId": 1,
    "userId": 2
  }'
```

### 7. Tagger: View Assigned Files
```bash
curl -X GET https://localhost:5001/api/tagger/my-files \
  -H "Authorization: Bearer TAGGER_TOKEN"
```

### 8. Tagger: Get File Preview URL
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
    "fileName": "document.pdf",
    "blobName": "document.pdf",
    "previewUrl": "https://storage.blob.core.windows.net/files/document.pdf?sv=2021-12-02&se=...",
    "expiresAt": "2024-01-15T15:30:00Z",
    "fileSize": 1024000,
    "contentType": "application/pdf"
  }
}
```

The `previewUrl` is a secure SAS (Shared Access Signature) URL that grants temporary read access to the file. It expires after the specified minutes (default: 60 minutes).

### 9. Tagger: Add Tags to File
```bash
curl -X POST https://localhost:5001/api/tagger/files/1/tags \
  -H "Authorization: Bearer TAGGER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "tags": [
      {"tagKey": "category", "tagValue": "invoice"},
      {"tagKey": "year", "tagValue": "2024"},
      {"tagKey": "department", "tagValue": "finance"}
    ]
  }'
```

### 10. Tagger: Complete File Tagging
```bash
curl -X POST https://localhost:5001/api/tagger/files/1/complete \
  -H "Authorization: Bearer TAGGER_TOKEN"
```

### 11. Admin: View Tagging Progress
```bash
curl -X GET https://localhost:5001/api/admin/tagging-progress \
  -H "Authorization: Bearer ADMIN_TOKEN"
```

## Architecture & Design Patterns

- **Options Pattern**: Type-safe configuration management using `IOptions<T>`
  - `JwtSettings`: JWT authentication configuration
  - `AzureBlobStorageSettings`: Azure Blob Storage connection settings
  - `DefaultAdminSettings`: Default admin user configuration
- **Dependency Injection**: Full DI implementation for services and repositories
- **Repository Pattern**: Clean separation of data access logic
- **DTO Pattern**: Data Transfer Objects for API requests/responses

## Security Features

- **JWT Bearer Authentication**: Secure token-based authentication
- **Role-Based Authorization**: Separate permissions for Admin and Tagger roles
- **SAS Token Generation**: Temporary, secure URLs for file access
  - Configurable expiry time (default: 60 minutes)
  - Read-only permissions
  - Automatic URL generation with Azure Storage SAS
- **Access Control**: Taggers can only preview files assigned to them
- **Password Hashing**: BCrypt for secure password storage
- **CORS Configuration**: Configurable cross-origin resource sharing
- **Soft Delete**: Users are deactivated rather than deleted
- **Type-Safe Configuration**: Options pattern prevents configuration errors

## Document Preview

The API provides secure, temporary URLs for document preview:

**Key Features:**
- **SAS (Shared Access Signature) URLs**: Temporary, signed URLs that expire automatically
- **Configurable Expiry**: Set expiry time from 1 to several hours (default: 60 minutes)
- **Read-Only Access**: Preview URLs only allow reading, not modification
- **Role-Based Access**:
  - Admins can preview any blob file (even unassigned/unimported ones)
  - Taggers can only preview files assigned to them
- **No Import Required**: Admins can preview blob storage files directly without importing to database
- **Supported File Types**: Works with any file type stored in Azure Blob Storage:
  - PDF documents
  - Images (JPG, PNG, GIF, etc.)
  - Markdown files (.md)
  - Text files
  - Office documents
  - Any other file type with proper content type
- **Frontend Integration**: Use the preview URL directly in:
  - `<iframe>` for PDF documents and markdown viewers
  - `<img>` for images
  - Direct download links
  - PDF viewer libraries (e.g., PDF.js, React-PDF)
  - Markdown renderers for .md files

**Usage Examples:**

```javascript
// Fetch preview URL from API
const response = await fetch(`/api/tagger/files/1/preview?expiryMinutes=60`, {
  headers: { 'Authorization': `Bearer ${token}` }
});
const { previewUrl, expiresAt } = await response.json();

// For PDF files
<iframe src={previewUrl} width="100%" height="600px" />
<embed src={previewUrl} type="application/pdf" width="100%" height="600px" />

// For images
<img src={previewUrl} alt="Document" />

// For markdown files - fetch content and render
const markdownResponse = await fetch(previewUrl);
const markdownText = await markdownResponse.text();
// Then use a markdown renderer like react-markdown
<ReactMarkdown>{markdownText}</ReactMarkdown>
```

## Database Schema

### Users Table
- Id (PK)
- Username (Unique)
- Email (Unique)
- PasswordHash
- Role (Admin/Tagger)
- CreatedAt
- LastLoginAt
- IsActive

### FileMetadata Table
- Id (PK)
- FileName
- FileUrl
- BlobName
- FileSize
- ContentType
- UploadedAt
- Status (Unassigned/Assigned/InProgress/Completed)
- TaggingCompletedAt

### FileAssignments Table
- Id (PK)
- FileMetadataId (FK)
- UserId (FK)
- AssignedAt
- AssignedByUserId
- CompletedAt
- IsCompleted

### FileTags Table
- Id (PK)
- FileMetadataId (FK)
- TagKey
- TagValue
- CreatedByUserId
- CreatedAt

## Technologies Used

- ASP.NET Core 8.0 Web API
- Entity Framework Core 8.0
- SQLite
- JWT Bearer Authentication
- Azure Storage Blobs SDK
- BCrypt.Net for password hashing
- Swagger/OpenAPI for API documentation

## License

This project is provided as-is for use in your organization.
