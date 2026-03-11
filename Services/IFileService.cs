using MetadataTagging.DTOs;

namespace MetadataTagging.Services;

public interface IFileService
{
    Task<IEnumerable<FileMetadataDto>> GetAllFilesAsync();
    Task<FileMetadataDto?> GetFileByIdAsync(int fileId);
    Task<FileMetadataDto?> GetFileByBlobNameAsync(string blobName);
    Task<FileMetadataDto> CreateFileMetadataAsync(CreateFileMetadataRequest request);
    Task<FileMetadataDto> ImportFileFromBlobAsync(BlobFileDto blobFile);
    Task<bool> AssignFileToUserAsync(int fileId, int userId, int adminId);
    Task<bool> AssignBlobFileToUserAsync(string blobName, int userId, int adminId);
    Task<AssignMultipleFilesResult> AssignMultipleFilesToUserAsync(List<string> blobNames, int userId, int adminId);
    Task<IEnumerable<FileMetadataDto>> GetFilesAssignedToUserAsync(int userId);
    Task<bool> AddTagsToFileAsync(int fileId, int userId, List<TagDto> tags);
    Task<bool> CompleteFileTaggingAsync(int fileId, int userId);
    Task<IEnumerable<TaggingProgressDto>> GetTaggingProgressAsync();
    Task<IEnumerable<FileMetadataDto>> GetUnassignedFilesAsync();
    Task<int> SyncFilesFromBlobStorageAsync();
    Task<bool> UpdateAudioMetadataAsync(int fileId, double durationSeconds);
}
