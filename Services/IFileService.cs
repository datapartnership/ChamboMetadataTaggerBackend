using MetadataTagging.DTOs;

namespace MetadataTagging.Services;

public interface IFileService
{
    Task<PagedResponse<FileMetadataDto>> GetAllFilesAsync(PaginationParams pagination);
    Task<FileMetadataDto?> GetFileByIdAsync(int fileId, int? userId = null);
    Task<FileMetadataDto?> GetFileByBlobNameAsync(string blobName);
    Task<FileMetadataDto> CreateFileMetadataAsync(CreateFileMetadataRequest request);
    Task<FileMetadataDto> ImportFileFromBlobAsync(BlobFileDto blobFile);
    Task<bool> AssignFileToUserAsync(int fileId, int userId, int adminId);
    Task<bool> AssignBlobFileToUserAsync(string blobName, int userId, int adminId);
    Task<AssignMultipleFilesResult> AssignMultipleFilesToUserAsync(List<string> blobNames, int userId, int adminId);
    Task<PagedResponse<FileMetadataDto>> GetFilesAssignedToUserAsync(int userId, PaginationParams pagination);
    Task<bool> AddTagsToFileAsync(int fileId, int userId, List<TagDto> tags);
    Task<bool> CompleteFileTaggingAsync(int fileId, int userId);
    Task<PagedResponse<TaggingProgressDto>> GetTaggingProgressAsync(PaginationParams pagination);
    Task<PagedResponse<FileMetadataDto>> GetUnassignedFilesAsync(PaginationParams pagination);
    Task<int> SyncFilesFromBlobStorageAsync();
    Task<bool> UpdateAudioMetadataAsync(int fileId, double durationSeconds);
}
