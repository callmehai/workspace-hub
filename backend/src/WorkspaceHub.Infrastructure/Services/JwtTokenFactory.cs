using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Infrastructure.Services;

/// <summary>
/// Nguồn duy nhất sinh access token JWT (SCRUM-63 review #3). Đọc Jwt config 1 lần ở ctor.
/// </summary>
public class JwtTokenFactory : IJwtTokenFactory
{
    private readonly SymmetricSecurityKey _signingKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _accessTtl;

    public JwtTokenFactory(IConfiguration config)
    {
        var jwt = config.GetSection("Jwt");
        var secret = jwt["Secret"] ?? throw new InvalidOperationException("Jwt:Secret is not configured");
        if (secret.Length < 32)
            throw new InvalidOperationException("Jwt:Secret must be at least 32 characters");

        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        _issuer = jwt["Issuer"] ?? "WorkspaceHub";
        _audience = jwt["Audience"] ?? "WorkspaceHub";
        _accessTtl = int.TryParse(jwt["ExpiresIn"], out var v) ? v : 900;
    }

    public (string Token, int ExpiresInSeconds) CreateAccessToken(User user)
    {
        var creds = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _issuer, audience: _audience, claims: claims,
            expires: DateTime.UtcNow.AddSeconds(_accessTtl), signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), _accessTtl);
    }
}
