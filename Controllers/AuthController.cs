using Microsoft.AspNetCore.Mvc;
using MetadataTagging.DTOs;
using MetadataTagging.Services;

namespace MetadataTagging.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("login")]
    public async Task<ActionResult<ApiResponse<LoginResponse>>> Login([FromBody] LoginRequest request)
    {
        try
        {
            var token = await _authService.AuthenticateAsync(request.Email, request.Password);

            if (token == null)
            {
                return Unauthorized(ApiResponse<LoginResponse>.ErrorResponse("Invalid email or password"));
            }

            var user = await _authService.GetUserByEmailAsync(request.Email);

            if (user == null)
            {
                return Unauthorized(ApiResponse<LoginResponse>.ErrorResponse("User not found"));
            }

            var response = new LoginResponse
            {
                Token = token,
                User = new UserDto
                {
                    Id = user.Id,
                    Username = user.Username,
                    Email = user.Email,
                    Role = user.Role,
                    CreatedAt = user.CreatedAt,
                    LastLoginAt = user.LastLoginAt,
                    IsActive = user.IsActive
                }
            };

            return Ok(ApiResponse<LoginResponse>.SuccessResponse(response, "Login successful"));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<LoginResponse>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }
}
