using MetadataTagging.DTOs;
using MetadataTagging.Models;

namespace MetadataTagging.Services;

public interface IUserService
{
    Task<IEnumerable<UserDto>> GetAllUsersAsync();
    Task<UserDto?> GetUserByIdAsync(int userId);
    Task<UserDto> CreateUserAsync(CreateUserRequest request);
    Task<BulkCreateUsersResponse> BulkCreateUsersAsync(IEnumerable<CreateUserRequest> requests);
    Task<bool> UpdateUserAsync(int userId, UpdateUserRequest request);
    Task<bool> DeleteUserAsync(int userId);
    Task<IEnumerable<UserDto>> GetTaggersAsync();
}
