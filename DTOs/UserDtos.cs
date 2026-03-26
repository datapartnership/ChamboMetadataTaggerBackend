namespace MetadataTagging.DTOs;

public class UserDto
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public required string Email { get; set; }
    public required string Role { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public bool IsActive { get; set; }
}

public class CreateUserRequest
{
    public required string Username { get; set; }
    public required string Email { get; set; }
    public required string Password { get; set; }
    public required string Role { get; set; }
}

public class UpdateUserRequest
{
    public string? Email { get; set; }
    public string? Password { get; set; }
    public string? Role { get; set; }
}

public class BulkCreateUsersRequest
{
    public required List<CreateUserRequest> Users { get; set; }
}

public class BulkCreateUserResult
{
    public string Email { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public bool Success { get; set; }
    public UserDto? User { get; set; }
    public string? Error { get; set; }
}

public class BulkCreateUsersResponse
{
    public List<BulkCreateUserResult> Results { get; set; } = new();
    public int SucceededCount { get; set; }
    public int FailedCount { get; set; }
}
