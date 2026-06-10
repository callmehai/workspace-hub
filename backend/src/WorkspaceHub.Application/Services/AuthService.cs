using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FluentValidation;
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
/// Business logic đăng ký / đăng nhập.
/// Hash password bằng BCrypt (cost 12), phát JWT qua JwtSecurityTokenHandler.
/// </summary>
public class AuthService : IAuthService
{
    private readonly IUserRepository _users;
    private readonly IConfiguration _config;
    private readonly IValidator<RegisterRequest> _registerValidator;
    private readonly IValidator<LoginRequest> _loginValidator;

    public AuthService(
        IUserRepository users,
        IConfiguration config,
        IValidator<RegisterRequest> registerValidator,
        IValidator<LoginRequest> loginValidator)
    {
        _users = users;
        _config = config;
        _registerValidator = registerValidator;
        _loginValidator = loginValidator;
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

        var (token, expiresIn) = GenerateJwtToken(user);
        return new AuthResponse(token, expiresIn, MapToDto(user));
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        await _loginValidator.ValidateAndThrowAsync(request, ct);

        var email = request.Email.Trim().ToLowerInvariant();

        var user = await _users.GetByEmailAsync(email, ct);
        if (user is null)
            throw new UnauthorizedException("Invalid credentials");

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedException("Invalid credentials");

        if (!user.IsActive)
            throw new UnauthorizedException("Account is locked");

        user.LastLoginAt = DateTime.UtcNow;
        await _users.SaveChangesAsync(ct);

        var (token, expiresIn) = GenerateJwtToken(user);
        return new AuthResponse(token, expiresIn, MapToDto(user));
    }

    public async Task<UserDto> GetMeAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct)
            ?? throw new NotFoundException($"User {userId} not found");

        return MapToDto(user);
    }

    // ── private helpers ──────────────────────────────────────────────

    private (string token, int expiresIn) GenerateJwtToken(User user)
    {
        var jwtSection = _config.GetSection("Jwt");

        var secret = jwtSection["Secret"]
            ?? throw new InvalidOperationException("Jwt:Secret is not configured");
        if (secret.Length < 32)
            throw new InvalidOperationException("Jwt:Secret must be at least 32 characters");

        var issuer = jwtSection["Issuer"];
        var audience = jwtSection["Audience"];
        var expiresIn = int.TryParse(jwtSection["ExpiresIn"], out var e) ? e : 3600;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddSeconds(expiresIn),
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresIn);
    }

    private static UserDto MapToDto(User user)
        => new(user.Id, user.Email, user.FullName, user.Role.ToString());
}
