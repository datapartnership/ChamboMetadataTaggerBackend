using Microsoft.EntityFrameworkCore;
using MetadataTagging.Data;
using MetadataTagging.DTOs;
using MetadataTagging.Models;

namespace MetadataTagging.Services;

public class UserService : IUserService
{
    private readonly ApplicationDbContext _context;

    public UserService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResponse<UserDto>> GetAllUsersAsync(PaginationParams pagination)
    {
        IQueryable<User> query = _context.Users.Where(u => u.IsActive);

        query = pagination.SortBy?.ToLowerInvariant() switch
        {
            "username" => pagination.IsDescending
                ? query.OrderByDescending(u => u.Username)
                : query.OrderBy(u => u.Username),
            "email" => pagination.IsDescending
                ? query.OrderByDescending(u => u.Email)
                : query.OrderBy(u => u.Email),
            "role" => pagination.IsDescending
                ? query.OrderByDescending(u => u.Role)
                : query.OrderBy(u => u.Role),
            "createdat" => pagination.IsDescending
                ? query.OrderByDescending(u => u.CreatedAt)
                : query.OrderBy(u => u.CreatedAt),
            _ => query.OrderBy(u => u.Username)
        };

        var totalCount = await query.CountAsync();

        var users = await query
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(u => new UserDto
            {
                Id = u.Id,
                Username = u.Username,
                Email = u.Email,
                Role = u.Role,
                CreatedAt = u.CreatedAt,
                LastLoginAt = u.LastLoginAt,
                IsActive = u.IsActive
            })
            .ToListAsync();

        return new PagedResponse<UserDto>
        {
            Items = users,
            Page = pagination.Page,
            PageSize = pagination.PageSize,
            TotalCount = totalCount
        };
    }

    public async Task<UserDto?> GetUserByIdAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId);

        if (user == null || !user.IsActive)
        {
            return null;
        }

        return new UserDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            Role = user.Role,
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt,
            IsActive = user.IsActive
        };
    }

    public async Task<UserDto> CreateUserAsync(CreateUserRequest request)
    {
        var existingUser = await _context.Users
            .FirstOrDefaultAsync(u => u.Username == request.Username || u.Email == request.Email);

        if (existingUser != null)
        {
            throw new InvalidOperationException("User with this username or email already exists");
        }

        var user = new User
        {
            Username = request.Username,
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = request.Role,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        return new UserDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            Role = user.Role,
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt,
            IsActive = user.IsActive
        };
    }

    public async Task<BulkCreateUsersResponse> BulkCreateUsersAsync(IEnumerable<CreateUserRequest> requests)
    {
        var response = new BulkCreateUsersResponse();

        foreach (var request in requests)
        {
            var result = new BulkCreateUserResult
            {
                Email = request.Email,
                Username = request.Username
            };

            try
            {
                var user = await CreateUserAsync(request);
                result.Success = true;
                result.User = user;
            }
            catch (InvalidOperationException ex)
            {
                result.Success = false;
                result.Error = ex.Message;
            }

            response.Results.Add(result);
        }

        response.SucceededCount = response.Results.Count(r => r.Success);
        response.FailedCount = response.Results.Count(r => !r.Success);

        return response;
    }

    public async Task<bool> UpdateUserAsync(int userId, UpdateUserRequest request)
    {
        var user = await _context.Users.FindAsync(userId);

        if (user == null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.Email) && request.Email != user.Email)
        {
            var emailExists = await _context.Users
                .AnyAsync(u => u.Email == request.Email && u.Id != userId);

            if (emailExists)
            {
                throw new InvalidOperationException("Email already in use");
            }

            user.Email = request.Email;
        }

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        }

        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            user.Role = request.Role;
        }

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteUserAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId);

        if (user == null)
        {
            return false;
        }

        user.IsActive = false;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<PagedResponse<UserDto>> GetTaggersAsync(PaginationParams pagination)
    {
        IQueryable<User> query = _context.Users
            .Where(u => u.Role == UserRoles.Tagger && u.IsActive);

        query = pagination.SortBy?.ToLowerInvariant() switch
        {
            "username" => pagination.IsDescending
                ? query.OrderByDescending(u => u.Username)
                : query.OrderBy(u => u.Username),
            "email" => pagination.IsDescending
                ? query.OrderByDescending(u => u.Email)
                : query.OrderBy(u => u.Email),
            "createdat" => pagination.IsDescending
                ? query.OrderByDescending(u => u.CreatedAt)
                : query.OrderBy(u => u.CreatedAt),
            _ => query.OrderBy(u => u.Username)
        };

        var totalCount = await query.CountAsync();

        var taggers = await query
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(u => new UserDto
            {
                Id = u.Id,
                Username = u.Username,
                Email = u.Email,
                Role = u.Role,
                CreatedAt = u.CreatedAt,
                LastLoginAt = u.LastLoginAt,
                IsActive = u.IsActive
            })
            .ToListAsync();

        return new PagedResponse<UserDto>
        {
            Items = taggers,
            Page = pagination.Page,
            PageSize = pagination.PageSize,
            TotalCount = totalCount
        };
    }
}
