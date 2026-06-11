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
    private readonly IOAuthConnectionRepository _oauthConnections;
    private readonly ITokenProtector _tokenProtector;
    private readonly IDistributedCache _cache;
    private readonly IReadOnlyDictionary<string, IProviderStrategy> _strategies;
    private readonly IConfiguration _config;

    public ConnectionsService(
        IIntegrationRepository integrations,
        IOAuthConnectionRepository oauthConnections,
        ITokenProtector tokenProtector,
        IDistributedCache cache,
        IEnumerable<IProviderStrategy> strategies,
        IConfiguration config)
    {
        _integrations = integrations;
        _oauthConnections = oauthConnections;
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
        var payload = JsonSerializer.Serialize(new OAuthStatePayload(integrationKey, userId));
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
        var redirectUri = _config[$"Dev:{integrationKey}:RedirectUri"]
            ?? throw new BusinessRuleException($"Chưa cấu hình RedirectUri cho '{integrationKey}'");

        // PRODUCTION:
        // var clientId     = _tokenProtector.Unprotect(integration.ClientIdEncrypted);
        // var clientSecret = _tokenProtector.Unprotect(integration.ClientSecretEncrypted);
        // var redirectUri  = _config[$"{integrationKey}:RedirectUri"] ?? throw new ...;

        // Step 4 — Delegate provider-specific exchange to strategy.
        var context = new CompleteContext(code, clientId, clientSecret, redirectUri, integration);
        var tokenResult = await strategy.ExchangeCodeAsync(context, ct);

        // Step 5 — Check duplicate.
        var existing = await _oauthConnections.GetByUniqueKeyAsync(
            userId, integration.Id, tokenResult.ProviderAccountId, ct);
        if (existing is not null)
            throw new ConflictException($"Tài khoản {integrationKey} này đã được kết nối");

        // Step 6 — Encrypt tokens.
        var accessTokenEncrypted = _tokenProtector.Protect(tokenResult.AccessToken);
        var refreshTokenEncrypted = _tokenProtector.Protect(tokenResult.RefreshToken ?? string.Empty);

        // Step 7 — Tạo OAuthConnection.
        var connection = new OAuthConnection
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            IntegrationId = integration.Id,
            ProviderAccountId = tokenResult.ProviderAccountId,
            AccessTokenEncrypted = accessTokenEncrypted,
            RefreshTokenEncrypted = refreshTokenEncrypted,
            ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResult.ExpiresIn),
            Scopes = tokenResult.RawScopes,
            Status = ConnectionStatus.Active,
            LastRefreshedAt = null
        };

        // Step 8 — Populate ServiceConnections theo scope thực tế provider cấp.
        ServiceConnectionSync.ApplyGrantedScopes(connection, tokenResult.GrantedServices);

        // Step 9 — Lưu DB.
        await _oauthConnections.AddAsync(connection, ct);
        await _oauthConnections.SaveChangesAsync(ct);

        var services = connection.ServiceConnections
            .Select(sc => new ServiceConnectionResult(sc.Id, sc.ServiceType.ToString(), sc.IsEnabled))
            .ToList();

        return new CompleteConnectionResult(
            connection.Id,
            integrationKey,
            tokenResult.ProviderAccountId,
            connection.Scopes,
            connection.Status.ToString(),
            services);
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
