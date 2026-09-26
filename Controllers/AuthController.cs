using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using MetadataTagging.DTOs;
using MetadataTagging.Services;
using MetadataTagger.Options;

namespace MetadataTagging.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly AuthenticationOptions _authenticationOptions;

    public AuthController(IAuthService authService, IOptions<AuthenticationOptions> authenticationOptions)
    {
        _authService = authService;
        _authenticationOptions = authenticationOptions.Value;
    }

    [HttpPost("login")]
    public async Task<ActionResult<ApiResponse<LoginResponse>>> Login([FromBody] LoginRequest request)
    {
        if (!_authenticationOptions.IsJwt)
        {
            return BadRequest(ApiResponse<LoginResponse>.ErrorResponse(
                $"Email/password login is only available when Authentication:Method is Jwt (current: {_authenticationOptions.Method})."));
        }

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

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<ApiResponse<UserDto>>> Me()
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdValue, out var userId))
        {
            return Unauthorized(ApiResponse<UserDto>.ErrorResponse("Authenticated user identifier is invalid"));
        }

        var user = await _authService.GetUserByIdAsync(userId);
        if (user == null || !user.IsActive)
        {
            return Unauthorized(ApiResponse<UserDto>.ErrorResponse("User is not authorized for this application"));
        }

        return Ok(ApiResponse<UserDto>.SuccessResponse(new UserDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            Role = user.Role,
            CreatedAt = user.CreatedAt,
            IsActive = user.IsActive
        }));
    }
}
