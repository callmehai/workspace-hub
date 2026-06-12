using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkspaceHub.Application.DTOs.Auth;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Api.Controllers;

/// <summary>
/// Auth endpoints: register + login + me. Controller mỏng — business logic nằm trong AuthService.
/// </summary>
public class AuthController : ApiControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth)
    {
        _auth = auth;
    }

    /// <summary>POST /api/auth/register — tạo tài khoản mới, trả JWT.</summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponse), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(409)]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        var result = await _auth.RegisterAsync(request, ct);
        return StatusCode(201, result);
    }

    /// <summary>POST /api/auth/login — đăng nhập, trả JWT.</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), 200)]
    [ProducesResponseType(401)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct)
        => Ok(await _auth.LoginAsync(request, ct));

    /// <summary>GET /api/auth/me — returns the current authenticated user's profile.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserDto), 200)]
    [ProducesResponseType(401)]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct)
        => Ok(await _auth.GetMeAsync(CurrentUserId, ct));

    /// <summary>POST /api/auth/google/start — returns Google Sign-In authorization URL.</summary>
    [HttpPost("google/start")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(GoogleAuthStartResponse), 200)]
    public async Task<ActionResult<GoogleAuthStartResponse>> GoogleStart(CancellationToken ct)
        => Ok(await _auth.GoogleStartAsync(ct));

    /// <summary>POST /api/auth/google/callback — exchange code, verify, find/create user, issue JWT.</summary>
    [HttpPost("google/callback")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<ActionResult<AuthResponse>> GoogleCallback(
        [FromBody] GoogleCallbackRequest request,
        CancellationToken ct)
        => Ok(await _auth.GoogleCallbackAsync(request.Code, request.State, ct));
}
