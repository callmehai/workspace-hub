using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.OAuth.Core;
using WorkspaceHub.Application.Security;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Services;

/// <summary>
/// Dispatcher — nhận request từ controller, resolve đúng IProviderStrategy theo integrationKey,
/// delegate việc build URL xuống strategy. Thêm provider mới = thêm strategy + đăng ký DI, không sửa file này.
/// </summary>
public class ConnectionsService : IConnectionsService
{
    private readonly IIntegrationRepository _integrations;
    private readonly IConnectionRepository _connections;
    private readonly ITokenProtector _tokenProtector;
    private readonly IDistributedCache _cache;
    private readonly IReadOnlyDictionary<string, IProviderStrategy> _strategies;
    private readonly IConfiguration _config;

    public ConnectionsService(
        IIntegrationRepository integrations,
        IConnectionRepository connections,
        ITokenProtector tokenProtector,
        IDistributedCache cache,
        IEnumerable<IProviderStrategy> strategies,
        IConfiguration config)
    {
        _integrations = integrations;
        _connections = connections;
        _tokenProtector = tokenProtector;
        _cache = cache;
        _strategies = strategies.ToDictionary(s => s.ProviderKey, StringComparer.OrdinalIgnoreCase);
        _config = config;
    }

    public async Task<InitiateConnectionResult> InitiateConnectionAsync(
        string integrationKey,
        string redirectUri,
        Guid userId,
        CancellationToken ct = default)
    {
        var integration = await _integrations.GetByKeyAsync(integrationKey, ct)
            ?? throw new NotFoundException($"Integration '{integrationKey}' không tồn tại");

        if (!integration.IsEnabled)
            throw new BusinessRuleException("Integration đang bị disabled");

        if (!_strategies.TryGetValue(integrationKey, out var strategy))
            throw new BusinessRuleException($"Provider '{integrationKey}' chưa được hỗ trợ");

        // DEV: đọc plaintext từ appsettings.Development.json để test nhanh không cần encrypt DB.
        var clientId = _config[$"Dev:{integrationKey}:ClientId"]
            ?? throw new BusinessRuleException($"Chưa cấu hình ClientId cho '{integrationKey}'");

        // PRODUCTION: decrypt từ DB bằng ITokenProtector.
        // var clientId = _tokenProtector.Unprotect(integration.ClientIdEncrypted);

        var state = Guid.NewGuid().ToString("N");

        // Lưu cả integrationKey lẫn userId — callback xác minh đúng user tạo ra state này.
        var payload = JsonSerializer.Serialize(new OAuthStatePayload(integrationKey, userId, redirectUri));
        var cacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        };
        await _cache.SetStringAsync($"oauth:state:{state}", payload, cacheOptions, ct);

        var context = new ProviderStrategyContext(clientId, redirectUri, state, integration);
        return await strategy.BuildAuthUrlAsync(context, ct);
    }

    public async Task<CompleteConnectionResult> CompleteConnectionAsync(
        string code,
        string state,
        Guid userId,
        CancellationToken ct = default)
    {
        // Step 1 — Verify + xoá state (one-time use).
        var cacheKey = $"oauth:state:{state}";
        var cached = await _cache.GetStringAsync(cacheKey, ct);
        if (cached is null)
            throw new CsrfException("State không hợp lệ hoặc đã hết hạn");

        await _cache.RemoveAsync(cacheKey, ct);

        var payload = JsonSerializer.Deserialize<OAuthStatePayload>(cached)
            ?? throw new CsrfException("State không hợp lệ hoặc đã hết hạn");

        // Xác minh state này đúng là do userId hiện tại tạo ra — chặn OAuth session hijacking.
        if (payload.UserId != userId)
            throw new CsrfException("State không hợp lệ hoặc đã hết hạn");

        var integrationKey = payload.IntegrationKey;
        var redirectUri = payload.RedirectUri;

        // Step 2 — Resolve strategy.
        if (!_strategies.TryGetValue(integrationKey, out var strategy))
            throw new BusinessRuleException($"Provider '{integrationKey}' chưa được hỗ trợ");

        // Step 3 — Load Integration + decrypt credentials.
        var integration = await _integrations.GetByKeyAsync(integrationKey, ct)
            ?? throw new NotFoundException($"Integration '{integrationKey}' không tồn tại");

        // DEV: đọc plaintext từ config; PROD: bỏ comment block bên dưới.
        var clientId = _config[$"Dev:{integrationKey}:ClientId"]
            ?? throw new BusinessRuleException($"Chưa cấu hình ClientId cho '{integrationKey}'");
        var clientSecret = _config[$"Dev:{integrationKey}:ClientSecret"]
            ?? throw new BusinessRuleException($"Chưa cấu hình ClientSecret cho '{integrationKey}'");

        // PRODUCTION:
        // var clientId     = _tokenProtector.Unprotect(integration.ClientIdEncrypted);
        // var clientSecret = _tokenProtector.Unprotect(integration.ClientSecretEncrypted);

        // Step 4 — Delegate provider-specific exchange to strategy.
        var context = new CompleteContext(code, clientId, clientSecret, redirectUri, integration);
        var tokenResult = await strategy.ExchangeCodeAsync(context, ct);

        if (tokenResult.GrantedServices.Count == 0)
            throw new BusinessRuleException("Provider không cấp quyền cho service nào");

        // Step 5 — Encrypt tokens.
        // RefreshToken null/empty (provider không trả, vd Google khi re-consent) → lưu chuỗi rỗng,
        // KHÔNG encrypt chuỗi rỗng. Convention: RefreshTokenEncrypted == "" nghĩa là "không có refresh token".
        var accessTokenEncrypted = _tokenProtector.Protect(tokenResult.AccessToken);
        var refreshTokenEncrypted = string.IsNullOrEmpty(tokenResult.RefreshToken)
            ? string.Empty
            : _tokenProtector.Protect(tokenResult.RefreshToken);
        var expiresAt = DateTime.UtcNow.AddSeconds(tokenResult.ExpiresIn);
        var provider = Enum.Parse<ProviderType>(integration.Provider);

        // Step 6 — Mô hình B: upsert 1 row Connection cho mỗi service được cấp.
        // Đã tồn tại (re-grant cùng account) → cập nhật token + reset Active, giữ cursor sync.
        var results = new List<ConnectionResult>();
        foreach (var serviceType in tokenResult.GrantedServices)
        {
            var connection = await _connections.GetByUniqueKeyAsync(
                userId, provider, serviceType, tokenResult.ProviderAccountId, ct);

            if (connection is null)
            {
                connection = new Connection
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    IntegrationId = integration.Id,
                    Provider = provider,
                    ServiceType = serviceType,
                    ProviderAccountId = tokenResult.ProviderAccountId,
                    AccessTokenEncrypted = accessTokenEncrypted,
                    RefreshTokenEncrypted = refreshTokenEncrypted,
                    ExpiresAt = expiresAt,
                    Status = ConnectionStatus.Active,
                    CursorValue = null, // null = sync lần đầu
                    CreatedAt = DateTime.UtcNow
                };
                await _connections.AddAsync(connection, ct);
            }
            else
            {
                connection.AccessTokenEncrypted = accessTokenEncrypted;
                // Re-grant thường KHÔNG trả refresh_token mới — chỉ ghi đè khi có, giữ token cũ khi rỗng.
                if (refreshTokenEncrypted.Length > 0)
                    connection.RefreshTokenEncrypted = refreshTokenEncrypted;
                connection.ExpiresAt = expiresAt;
                connection.Status = ConnectionStatus.Active;
                connection.LastError = null;
                _connections.Update(connection);
            }

            results.Add(new ConnectionResult(
                connection.Id, connection.ServiceType.ToString(), connection.Status.ToString()));
        }

        // Step 7 — Lưu DB.
        await _connections.SaveChangesAsync(ct);

        return new CompleteConnectionResult(integrationKey, tokenResult.ProviderAccountId, results);
    }

    public async Task SetCredentialsAsync(
        string integrationKey,
        string clientId,
        string clientSecret,
        CancellationToken ct = default)
    {
        var integration = await _integrations.GetByKeyAsync(integrationKey, ct)
            ?? throw new NotFoundException($"Integration '{integrationKey}' không tồn tại");

        integration.ClientIdEncrypted = _tokenProtector.Protect(clientId);
        integration.ClientSecretEncrypted = _tokenProtector.Protect(clientSecret);

        _integrations.Update(integration);
        await _integrations.SaveChangesAsync(ct);
    }
}
