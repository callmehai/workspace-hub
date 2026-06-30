using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Web;
using FluentValidation;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs.Auth;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Services;

/// <summary>
/// Business logic đăng ký / đăng nhập / Google Sign-In.
/// Hash password bằng BCrypt (cost 12), phát JWT qua JwtSecurityTokenHandler.
/// </summary>
public class AuthService : IAuthService
{
    private readonly IUserRepository _users;
    private readonly IConfiguration _config;
    private readonly IValidator<RegisterRequest> _registerValidator;
    private readonly IValidator<LoginRequest> _loginValidator;
    private readonly IDistributedCache _cache;
    private readonly IOAuthTokenClient _tokenClient;
    private readonly IGoogleTokenVerifier _googleTokenVerifier;
    private readonly IJwtTokenFactory _jwt;

    private const string GoogleAuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string GoogleTokenEndpoint = "https://oauth2.googleapis.com/token";

    public AuthService(
        IUserRepository users,
        IConfiguration config,
        IValidator<RegisterRequest> registerValidator,
        IValidator<LoginRequest> loginValidator,
        IDistributedCache cache,
        IOAuthTokenClient tokenClient,
        IGoogleTokenVerifier googleTokenVerifier,
        IJwtTokenFactory jwt)
    {
        _users = users;
        _config = config;
        _registerValidator = registerValidator;
        _loginValidator = loginValidator;
        _cache = cache;
        _tokenClient = tokenClient;
        _googleTokenVerifier = googleTokenVerifier;
        _jwt = jwt;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        await _registerValidator.ValidateAndThrowAsync(request, ct);

        var email = request.Email.Trim().ToLowerInvariant();

        if (await _users.EmailExistsAsync(email, ct))
            throw new ConflictException("Email already registered");

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = passwordHash,
            FullName = request.FullName.Trim(),
            Role = UserRole.User,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await _users.AddAsync(user, ct);
        await _users.SaveChangesAsync(ct);

        var (token, expiresIn) = _jwt.CreateAccessToken(user);
        return new AuthResponse(token, expiresIn, MapToDto(user));
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        await _loginValidator.ValidateAndThrowAsync(request, ct);

        var email = request.Email.Trim().ToLowerInvariant();

        var user = await _users.GetByEmailAsync(email, ct);
        if (user is null)
            throw new UnauthorizedException("Invalid credentials");

        // Google-only users have no password — reject local login attempt
        if (string.IsNullOrEmpty(user.PasswordHash))
            throw new UnauthorizedException("Invalid credentials");

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedException("Invalid credentials");

        return await SignInAsync(user, ct);
    }

    public async Task<UserDto> GetMeAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct)
            ?? throw new NotFoundException($"User {userId} not found");

        return MapToDto(user);
    }

    public async Task<GoogleAuthStartResponse> GoogleStartAsync(CancellationToken ct = default)
    {
        var clientId = _config["OAuth:google:ClientId"]
                       ?? _config["Google:ClientId"]
                       ?? throw new InvalidOperationException("Google ClientId is not configured.");

        var redirectUri = _config["Google:SignInRedirectUri"]
                          ?? _config["Google:RedirectUri"]
                          ?? throw new InvalidOperationException("Google RedirectUri is not configured.");

        var state = Guid.NewGuid().ToString("N");

        // Cache state with 10-minute TTL; value "signin" distinguishes from sync flow
        var cacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        };
        await _cache.SetStringAsync($"oauth:signin:state:{state}", "signin", cacheOptions, ct);

        // Build authorization URL — sign-in only: access_type=online, prompt=select_account
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["client_id"] = clientId;
        query["redirect_uri"] = redirectUri;
        query["response_type"] = "code";
        query["scope"] = "openid email profile";
        query["state"] = state;
        query["access_type"] = "online";
        query["prompt"] = "select_account";

        var authorizationUrl = $"{GoogleAuthEndpoint}?{query}";
        return new GoogleAuthStartResponse(authorizationUrl, state);
    }

    public async Task<AuthResponse> GoogleCallbackAsync(string code, string state, CancellationToken ct = default)
    {
        // Step 1 — Verify and consume CSRF state (one-time use)
        var cacheKey = $"oauth:signin:state:{state}";
        var cached = await _cache.GetStringAsync(cacheKey, ct);
        if (cached is null || cached != "signin")
            throw new CsrfException("State không hợp lệ hoặc đã hết hạn");

        await _cache.RemoveAsync(cacheKey, ct);

        // Step 2 — Read Google credentials from config
        var clientId = _config["OAuth:google:ClientId"]
                       ?? _config["Google:ClientId"]
                       ?? throw new InvalidOperationException("Google ClientId is not configured.");

        var clientSecret = _config["OAuth:google:ClientSecret"]
                           ?? _config["Google:ClientSecret"]
                           ?? throw new InvalidOperationException("Google ClientSecret is not configured.");

        var redirectUri = _config["Google:SignInRedirectUri"]
                          ?? _config["Google:RedirectUri"]
                          ?? throw new InvalidOperationException("Google RedirectUri is not configured.");

        // Step 3 — Exchange code → tokens via HTTP POST
        var formData = new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code"
        };

        string json;
        try
        {
            json = await _tokenClient.PostFormAsync(GoogleTokenEndpoint, formData, ct);
        }
        catch (HttpRequestException)
        {
            throw new BusinessRuleException("Google rejected the authorization code");
        }

        GoogleSignInTokenResponse tokenResponse;
        try
        {
            tokenResponse = JsonSerializer.Deserialize<GoogleSignInTokenResponse>(json)
                ?? throw new BusinessRuleException("Google rejected the authorization code");
        }
        catch (JsonException)
        {
            throw new BusinessRuleException("Google rejected the authorization code");
        }

        if (string.IsNullOrEmpty(tokenResponse.IdToken))
            throw new BusinessRuleException("Google did not return an id_token");

        // Step 4 — Verify id_token using Google.Apis.Auth (via IGoogleTokenVerifier)
        var (sub, email) = await _googleTokenVerifier.VerifyAsync(tokenResponse.IdToken, ct);

        // Step 5 — Lookup user by GoogleSub, then by email
        var user = await _users.GetByGoogleSubAsync(sub, ct);
        if (user is not null)
            return await SignInAsync(user, ct);

        user = await _users.GetByEmailAsync(email, ct);
        if (user is not null)
        {
            // Link Google account to existing local user
            user.GoogleSub = sub;
            user.AuthProvider = user.AuthProvider == AuthProvider.Local
                ? AuthProvider.Both
                : user.AuthProvider;
            // Nếu account bị khoá, SignInAsync throw trước SaveChanges → GoogleSub không được lưu (đúng ý).
            return await SignInAsync(user, ct);
        }

        // Step 6 — Create new Google-only user
        var newUser = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = null,
            FullName = email.Split('@')[0],
            AuthProvider = AuthProvider.Google,
            GoogleSub = sub,
            IsActive = true,
            Role = UserRole.User,
            CreatedAt = DateTime.UtcNow,
            LastLoginAt = DateTime.UtcNow
        };

        await _users.AddAsync(newUser, ct);
        await _users.SaveChangesAsync(ct);

        var (token, expiresIn) = _jwt.CreateAccessToken(newUser);
        return new AuthResponse(token, expiresIn, MapToDto(newUser));
    }

    // ── private helpers ──────────────────────────────────────────────

    /// <summary>
    /// Hoàn tất đăng nhập cho user đã tồn tại: chặn account bị khoá,
    /// cập nhật LastLoginAt, phát JWT. Dùng chung cho login local + Google Sign-In.
    /// </summary>
    private async Task<AuthResponse> SignInAsync(User user, CancellationToken ct)
    {
        if (!user.IsActive)
            throw new UnauthorizedException("Account is locked");

        user.LastLoginAt = DateTime.UtcNow;
        await _users.SaveChangesAsync(ct);

        var (token, expiresIn) = _jwt.CreateAccessToken(user);
        return new AuthResponse(token, expiresIn, MapToDto(user));
    }

    private static UserDto MapToDto(User user)
        => new(user.Id, user.Email, user.FullName, user.Role.ToString());

    /// <summary>Minimal token response for Google Sign-In (only need id_token).</summary>
    private class GoogleSignInTokenResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("id_token")]
        public string? IdToken { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}
