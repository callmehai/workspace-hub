using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs.Connections;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.OAuth.Core;
using WorkspaceHub.Application.Security;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Services;

/// <summary>Chọn đúng strategy theo provider, rồi để việc thực thi xuống strategy đó.</summary>
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
        string serviceType,
        string redirectUri,
        Guid userId,
        CancellationToken ct = default)
    {
        // Kiểm tra serviceType hợp lệ trước khi làm gì khác.
        if (!Enum.TryParse<ServiceType>(serviceType, ignoreCase: true, out _))
            throw new BusinessRuleException($"ServiceType '{serviceType}' không hợp lệ");

        var integration = await _integrations.GetByKeyAsync(integrationKey, ct)
            ?? throw new NotFoundException($"Integration '{integrationKey}' không tồn tại");

        if (!integration.IsEnabled)
            throw new BusinessRuleException("Integration đang bị disabled");

        if (!_strategies.TryGetValue(integrationKey, out var strategy))
            throw new BusinessRuleException($"Provider '{integrationKey}' chưa được hỗ trợ");

        var clientId = _config[$"OAuth:{integrationKey}:ClientId"]
            ?? throw new BusinessRuleException($"Chưa cấu hình ClientId cho '{integrationKey}'");

        var state = Guid.NewGuid().ToString("N");

        // Lưu thông tin phiên kết nối vào cache — dùng để xác minh khi Google redirect về.
        var payload = JsonSerializer.Serialize(new OAuthStatePayload(integrationKey, userId, redirectUri, serviceType));
        var cacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        };
        await _cache.SetStringAsync($"oauth:state:{state}", payload, cacheOptions, ct);

        var request = new BuildAuthUrlRequest(clientId, redirectUri, state, integration, serviceType);
        return await strategy.BuildAuthUrlAsync(request, ct);
    }

    public async Task<CompleteConnectionResult> CompleteConnectionAsync(
        string code,
        string state,
        Guid userId,
        CancellationToken ct = default)
    {
        // Bước 1 — Kiểm tra state còn hợp lệ, xoá ngay để không dùng lại được.
        var cacheKey = $"oauth:state:{state}";
        var cached = await _cache.GetStringAsync(cacheKey, ct);
        if (cached is null)
            throw new CsrfException("State không hợp lệ hoặc đã hết hạn");

        await _cache.RemoveAsync(cacheKey, ct);

        var payload = JsonSerializer.Deserialize<OAuthStatePayload>(cached)
            ?? throw new CsrfException("State không hợp lệ hoặc đã hết hạn");

        // Đảm bảo state này đúng là do user hiện tại tạo ra.
        if (payload.UserId != userId)
            throw new CsrfException("State không hợp lệ hoặc đã hết hạn");

        var integrationKey = payload.IntegrationKey;
        var redirectUri = payload.RedirectUri;

        // Bước 2 — Tìm strategy phù hợp với provider.
        if (!_strategies.TryGetValue(integrationKey, out var strategy))
            throw new BusinessRuleException($"Provider '{integrationKey}' chưa được hỗ trợ");

        // Bước 3 — Lấy thông tin integration và credentials từ config.
        var integration = await _integrations.GetByKeyAsync(integrationKey, ct)
            ?? throw new NotFoundException($"Integration '{integrationKey}' không tồn tại");

        var clientId = _config[$"OAuth:{integrationKey}:ClientId"]
            ?? throw new BusinessRuleException($"Chưa cấu hình ClientId cho '{integrationKey}'");
        var clientSecret = _config[$"OAuth:{integrationKey}:ClientSecret"]
            ?? throw new BusinessRuleException($"Chưa cấu hình ClientSecret cho '{integrationKey}'");

        // Bước 4 — Đổi code lấy token (logic riêng của từng provider).
        var request = new ExchangeCodeRequest(code, clientId, clientSecret, redirectUri, integration, payload.ServiceType);
        var tokenResult = await strategy.ExchangeCodeAsync(request, ct);

        if (tokenResult.GrantedServices.Count == 0)
            throw new BusinessRuleException("Provider không cấp quyền cho service nào");

        // Bước 5 — Mã hoá token trước khi lưu. RefreshToken rỗng thì giữ nguyên chuỗi rỗng.
        var accessTokenEncrypted = _tokenProtector.Protect(tokenResult.AccessToken);
        var refreshTokenEncrypted = string.IsNullOrEmpty(tokenResult.RefreshToken)
            ? string.Empty
            : _tokenProtector.Protect(tokenResult.RefreshToken);
        var expiresAt = DateTime.UtcNow.AddSeconds(tokenResult.ExpiresIn);
        var provider = Enum.Parse<ProviderType>(integration.Provider);

        // Bước 6 — Tạo một Connection riêng cho mỗi service được cấp quyền.
        var results = new List<ConnectionResult>();
        foreach (var serviceType in tokenResult.GrantedServices)
        {
            var existing = await _connections.GetByUniqueKeyAsync(
                userId, provider, serviceType, tokenResult.ProviderAccountId, ct);

            if (existing is not null)
                throw new ConflictException(
                    $"Bạn đã kết nối {serviceType} với tài khoản '{tokenResult.ProviderAccountId}' rồi. Hãy ngắt kết nối trước.");

            var connection = new Connection
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
                CursorValue = null,
                CreatedAt = DateTime.UtcNow
            };
            await _connections.AddAsync(connection, ct);

            results.Add(new ConnectionResult(
                connection.Id, connection.ServiceType.ToString(), connection.Status.ToString()));
        }

        // Bước 7 — Lưu tất cả vào DB.
        await _connections.SaveChangesAsync(ct);

        return new CompleteConnectionResult(integrationKey, tokenResult.ProviderAccountId, results);
    }

    public async Task<IntegrationResponse> ToggleIntegrationAsync(string key, bool isEnabled, CancellationToken ct = default)
    {
        var integration = await _integrations.GetByKeyAsync(key, ct)
            ?? throw new NotFoundException($"Integration '{key}' không tồn tại");

        integration.IsEnabled = isEnabled;
        _integrations.Update(integration);
        await _integrations.SaveChangesAsync(ct);

        return new IntegrationResponse(integration.Id, integration.Key, integration.DisplayName, integration.IsEnabled);
    }

}
