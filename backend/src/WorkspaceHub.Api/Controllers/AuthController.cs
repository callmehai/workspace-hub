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
    private readonly IRefreshTokenService _refreshTokens;

    public AuthController(IAuthService auth, AuthCookieService cookies, IRefreshTokenService refreshTokens)
    {
        _auth = auth;
        _cookies = cookies;
        _refreshTokens = refreshTokens;
    }

    /// <summary>POST /api/auth/register — tạo tài khoản mới; set cookie JWT.</summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResultDto), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(409)]
    public async Task<ActionResult<AuthResultDto>> Register(RegisterRequest request, CancellationToken ct)
    {
        var result = await _auth.RegisterAsync(request, ct);
        return StatusCode(201, await IssueCookiesAsync(result, ct));
    }

    /// <summary>POST /api/auth/login — đăng nhập; set cookie JWT.</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResultDto), 200)]
    [ProducesResponseType(401)]
    public async Task<ActionResult<AuthResultDto>> Login(LoginRequest request, CancellationToken ct)
        => Ok(await IssueCookiesAsync(await _auth.LoginAsync(request, ct), ct));

    /// <summary>GET /api/auth/me — returns the current authenticated user's profile.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserDto), 200)]
    [ProducesResponseType(401)]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct)
        => Ok(await _auth.GetMeAsync(CurrentUserId, ct));

    /// <summary>
    /// POST /api/auth/logout — revoke refresh token (Redis) + xoá cookie auth (SCRUM-62/63).
    /// AllowAnonymous: access token có thể đã hết hạn nhưng cookie vẫn còn → vẫn phải
    /// revoke + clear được thay vì trả 401.
    /// </summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var refreshToken = Request.Cookies[AuthCookieService.RefreshCookieName];
        await _refreshTokens.RevokeAsync(refreshToken, ct);
        _cookies.ClearAuthCookies(Response);
        return NoContent();
    }

    /// <summary>
    /// POST /api/auth/refresh — đổi refresh token (cookie wh_refresh) lấy access token mới
    /// + xoay refresh token (SCRUM-63). Token thiếu/không hợp lệ/đã revoke → 401.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResultDto), 200)]
    [ProducesResponseType(401)]
    public async Task<ActionResult<AuthResultDto>> Refresh(CancellationToken ct)
    {
        var refreshToken = Request.Cookies[AuthCookieService.RefreshCookieName];
        if (string.IsNullOrEmpty(refreshToken))
            return Unauthorized();

        var rotated = await _refreshTokens.ValidateAndRotateAsync(refreshToken, ct);

        _cookies.IssueAccessCookie(Response, rotated.AccessToken, rotated.AccessExpiresInSeconds);
        _cookies.IssueRefreshCookie(Response, rotated.RefreshToken, rotated.RefreshExpiresInSeconds);

        var user = new UserDto(rotated.User.Id, rotated.User.Email, rotated.User.FullName, rotated.User.Role);
        return Ok(new AuthResultDto(rotated.AccessExpiresInSeconds, user));
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
        => Ok(await IssueCookiesAsync(await _auth.GoogleCallbackAsync(request.Code, request.State, ct), ct));

    /// <summary>
    /// Set access token (cookie wh_access, SCRUM-62) + phát refresh token mới (cookie
    /// wh_refresh, SCRUM-63). Trả body không chứa token.
    /// </summary>
    private async Task<AuthResultDto> IssueCookiesAsync(AuthResponse result, CancellationToken ct)
    {
        _cookies.IssueAccessCookie(Response, result.AccessToken, result.ExpiresIn);

        var refresh = await _refreshTokens.IssueAsync(result.User.Id, ct);
        _cookies.IssueRefreshCookie(Response, refresh.RefreshToken, refresh.ExpiresInSeconds);

        return new AuthResultDto(result.ExpiresIn, result.User);
    }
}
