using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Microsoft.Extensions.Configuration;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Security;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Infrastructure.Services;

public class TokenService : ITokenService
{
    private readonly ITokenProtector _tokenProtector;
    private readonly IIntegrationRepository _integrations;
    private readonly IConnectionRepository _connections;
    private readonly IConfiguration _config;
    private static readonly TimeSpan Buffer = TimeSpan.FromMinutes(5);

    public TokenService(
        ITokenProtector tokenProtector,
        IIntegrationRepository integrations,
        IConnectionRepository connections,
        IConfiguration config)
    {
        _tokenProtector = tokenProtector;
        _integrations = integrations;
        _connections = connections;
        _config = config;
    }

    public async Task<string> GetFreshAccessTokenAsync(Connection connection, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // 1. Kiểm tra token còn hạn > 5 phút thì return ngay (không gọi mạng)
        if (connection.ExpiresAt - now > Buffer && !string.IsNullOrEmpty(connection.AccessTokenEncrypted))
        {
            return _tokenProtector.Unprotect(connection.AccessTokenEncrypted);
        }

        // 2. Giải mã RefreshTokenEncrypted
        if (string.IsNullOrEmpty(connection.RefreshTokenEncrypted))
        {
            throw new InvalidOperationException("Không thể refresh vì RefreshToken rỗng hoặc không tồn tại.");
        }
        var refreshToken = _tokenProtector.Unprotect(connection.RefreshTokenEncrypted);

        // 3. Lấy Integration để lấy credentials từ cấu hình (không lưu db)
        var integration = await _integrations.GetByIdAsync(connection.IntegrationId, ct)
            ?? throw new InvalidOperationException($"Không tìm thấy Integration {connection.IntegrationId}");

        var clientId = _config[$"OAuth:{integration.Key}:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException($"Chưa cấu hình ClientId cho provider '{integration.Key}'");

        var clientSecret = _config[$"OAuth:{integration.Key}:ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientSecret))
            throw new InvalidOperationException($"Chưa cấu hình ClientSecret cho provider '{integration.Key}'");

        try
        {
            // 4. Dùng thư viện Google API để đổi refresh token lấy access token mới
            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = clientId,
                    ClientSecret = clientSecret
                }
            });

            var tokenResponse = await flow.RefreshTokenAsync(connection.Id.ToString(), refreshToken, ct);

            if (string.IsNullOrEmpty(tokenResponse.AccessToken))
            {
                throw new InvalidOperationException("Google trả về access token rỗng khi refresh");
            }

            // 5. Cập nhật connection với access token mới
            connection.AccessTokenEncrypted = _tokenProtector.Protect(tokenResponse.AccessToken);
            connection.ExpiresAt = now.AddSeconds(tokenResponse.ExpiresInSeconds ?? 3600);
            
            // Lưu ý: entity Connection không có LastRefreshedAt nên không track thời điểm refresh ở đây.

            // Chỉ cập nhật refresh token nếu Google trả về một refresh token mới
            if (!string.IsNullOrEmpty(tokenResponse.RefreshToken))
            {
                connection.RefreshTokenEncrypted = _tokenProtector.Protect(tokenResponse.RefreshToken);
            }

            // 6. Lưu xuống DB qua Repository pattern
            _connections.Update(connection);
            await _connections.SaveChangesAsync(ct);

            return tokenResponse.AccessToken;
        }
        catch (TokenResponseException ex)
        {
            // Nếu lỗi làm mới (VD: bị thu hồi quyền), đổi Status thành Error
            connection.Status = ConnectionStatus.Error;
            connection.LastError = $"Lỗi refresh token: {ex.Message}";
            _connections.Update(connection);
            await _connections.SaveChangesAsync(ct);

            throw new InvalidOperationException("Quá trình refresh token thất bại, vui lòng kết nối lại.", ex);
        }
    }
}
