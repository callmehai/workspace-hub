using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkspaceHub.Api.Auth;
using WorkspaceHub.Application.DTOs.Auth;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Api.Controllers;

/// <summary>
/// Auth endpoints: register + login + me. Controller mỏng — business logic nằm trong AuthService.
/// SCRUM-62: access token được set vào HttpOnly cookie (không trả trong body).
/// </summary>
public class AuthController : ApiControllerBase
{
    private readonly IAuthService _auth;
    private readonly AuthCookieService _cookies;

    public AuthController(IAuthService auth, AuthCookieService cookies)
    {
        _auth = auth;
        _cookies = cookies;
    }

    /// <summary>POST /api/auth/register — tạo tài khoản mới; set cookie JWT.</summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResultDto), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(409)]
    public async Task<ActionResult<AuthResultDto>> Register(RegisterRequest request, CancellationToken ct)
    {
        var result = await _auth.RegisterAsync(request, ct);
        return StatusCode(201, IssueCookieAndStripToken(result));
    }

    /// <summary>POST /api/auth/login — đăng nhập; set cookie JWT.</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResultDto), 200)]
    [ProducesResponseType(401)]
    public async Task<ActionResult<AuthResultDto>> Login(LoginRequest request, CancellationToken ct)
        => Ok(IssueCookieAndStripToken(await _auth.LoginAsync(request, ct)));

    /// <summary>GET /api/auth/me — returns the current authenticated user's profile.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserDto), 200)]
    [ProducesResponseType(401)]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct)
        => Ok(await _auth.GetMeAsync(CurrentUserId, ct));

    /// <summary>POST /api/auth/logout — xoá cookie auth (SCRUM-62).</summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(204)]
    [ProducesResponseType(401)]
    public IActionResult Logout()
    {
        _cookies.ClearAuthCookies(Response);
        return NoContent();
    }
    /// <summary>POST /api/auth/google/start — returns Google Sign-In authorization URL.</summary>
    [HttpPost("google/start")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(GoogleAuthStartResponse), 200)]
    public async Task<ActionResult<GoogleAuthStartResponse>> GoogleStart(CancellationToken ct)
        => Ok(await _auth.GoogleStartAsync(ct));

    /// <summary>POST /api/auth/google/callback — exchange code, verify, find/create user; set cookie JWT.</summary>
    [HttpPost("google/callback")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResultDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<ActionResult<AuthResultDto>> GoogleCallback(
        [FromBody] GoogleCallbackRequest request,
        CancellationToken ct)
        => Ok(IssueCookieAndStripToken(await _auth.GoogleCallbackAsync(request.Code, request.State, ct)));

    /// <summary>Set access token vào HttpOnly cookie, trả body không chứa token (SCRUM-62).</summary>
    private AuthResultDto IssueCookieAndStripToken(AuthResponse result)
    {
        _cookies.IssueAccessCookie(Response, result.AccessToken, result.ExpiresIn);
        return new AuthResultDto(result.ExpiresIn, result.User);
    }
}
