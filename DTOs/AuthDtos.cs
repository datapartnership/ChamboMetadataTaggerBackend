namespace MetadataTagging.DTOs;

public class LoginRequest
{
    public required string Email { get; set; }
    public required string Password { get; set; }
}

public class LoginResponse
{
    public required string Token { get; set; }
    public required UserDto User { get; set; }
}
