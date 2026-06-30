using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Security;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Infrastructure.Services;

/// <summary>
/// Refresh access token Atlassian (offline_access). Atlassian dùng OAuth 2.0 chuẩn nên POST form
/// grant_type=refresh_token lên token endpoint của integration — khác Google (dùng SDK riêng).
/// Atlassian xoay vòng refresh token (rotating) → luôn lưu refresh token mới trả về.
/// </summary>
public class AtlassianTokenService : IAtlassianTokenService
{
    private readonly ITokenProtector _tokenProtector;
    private readonly IIntegrationRepository _integrations;
    private readonly IConnectionRepository _connections;
    private readonly IOAuthTokenClient _tokenClient;
    private readonly IConfiguration _config;
    private static readonly TimeSpan Buffer = TimeSpan.FromMinutes(5);

    // Atlassian rotating refresh token: 2 request đồng thời cùng refresh sẽ làm token thứ 2 dùng refresh token
    // đã bị vô hiệu. Khoá per-connection (static, sống theo process) để chỉ 1 refresh chạy mỗi connection.
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> RefreshLocks = new();

    public AtlassianTokenService(
        ITokenProtector tokenProtector,
        IIntegrationRepository integrations,
        IConnectionRepository connections,
        IOAuthTokenClient tokenClient,
        IConfiguration config)
    {
        _tokenProtector = tokenProtector;
        _integrations = integrations;
        _connections = connections;
        _tokenClient = tokenClient;
        _config = config;
    }

    public async Task<string> GetFreshAccessTokenAsync(Connection connection, CancellationToken ct = default)
    {
        // Fast-path: token còn hạn → trả ngay, không cần khoá.
        if (IsTokenFresh(connection))
            return _tokenProtector.Unprotect(connection.AccessTokenEncrypted);

        // Slow-path: khoá per-connection để chỉ 1 refresh chạy (rotating refresh token).
        var gate = RefreshLocks.GetOrAdd(connection.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            // Double-check: một request khác có thể vừa refresh xong khi ta chờ khoá.
            // Đọc lại bản tracked mới nhất từ DB để thấy token đã được cập nhật.
            var fresh = await _connections.GetByIdTrackedAsync(connection.Id, ct);
            if (fresh is not null && IsTokenFresh(fresh))
            {
                // Đồng bộ entity caller đang giữ với giá trị mới (token + expiry).
                connection.AccessTokenEncrypted = fresh.AccessTokenEncrypted;
                connection.RefreshTokenEncrypted = fresh.RefreshTokenEncrypted;
                connection.ExpiresAt = fresh.ExpiresAt;
                return _tokenProtector.Unprotect(fresh.AccessTokenEncrypted);
            }

            return await RefreshAndPersistAsync(connection, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private static bool IsTokenFresh(Connection connection) =>
        connection.ExpiresAt - DateTime.UtcNow > Buffer && !string.IsNullOrEmpty(connection.AccessTokenEncrypted);

    private async Task<string> RefreshAndPersistAsync(Connection connection, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        if (string.IsNullOrEmpty(connection.RefreshTokenEncrypted))
            throw new BusinessRuleException("RefreshToken Jira rỗng — cần kết nối lại.");

        var refreshToken = _tokenProtector.Unprotect(connection.RefreshTokenEncrypted);

        var integration = await _integrations.GetByIdAsync(connection.IntegrationId, ct)
            ?? throw new InvalidOperationException($"Không tìm thấy Integration {connection.IntegrationId}");

        var clientId = _config[$"OAuth:{integration.Key}:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException($"Chưa cấu hình ClientId cho provider '{integration.Key}'");

        var clientSecret = _config[$"OAuth:{integration.Key}:ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientSecret))
            throw new InvalidOperationException($"Chưa cấu hình ClientSecret cho provider '{integration.Key}'");

        var form = new Dictionary<string, string>
        {
            ["grant_type"]    = "refresh_token",
            ["client_id"]     = clientId,
            ["client_secret"] = clientSecret,
            ["refresh_token"] = refreshToken
        };

        string json;
        try
        {
            json = await _tokenClient.PostFormAsync(integration.TokenEndpoint, form, ct);
        }
        catch (HttpRequestException ex)
        {
            connection.Status = ConnectionStatus.Error;
            connection.LastError = $"Lỗi refresh token Atlassian: {ex.Message}";
            _connections.Update(connection);
            await _connections.SaveChangesAsync(ct);
            // Re-auth cần thiết → BusinessRule (422) thay vì 500.
            throw new BusinessRuleException("Refresh token Atlassian thất bại, vui lòng kết nối lại Jira.");
        }

        var token = JsonSerializer.Deserialize<AtlassianRefreshResponse>(json);
        if (token is null || string.IsNullOrEmpty(token.AccessToken))
            throw new ProviderException("Atlassian trả về access token rỗng khi refresh");

        connection.AccessTokenEncrypted = _tokenProtector.Protect(token.AccessToken);
        connection.ExpiresAt = now.AddSeconds(token.ExpiresIn > 0 ? token.ExpiresIn : 3600);

        // Atlassian rotate refresh token — lưu cái mới (cũ bị vô hiệu).
        if (!string.IsNullOrEmpty(token.RefreshToken))
            connection.RefreshTokenEncrypted = _tokenProtector.Protect(token.RefreshToken);

        _connections.Update(connection);
        await _connections.SaveChangesAsync(ct);

        return token.AccessToken;
    }

    private sealed class AtlassianRefreshResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("access_token")]  public string AccessToken { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("expires_in")]    public int ExpiresIn { get; set; }
    }
}
