using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.OAuth;

namespace WorkspaceHub.Application.Services;

/// <summary>
/// Dispatcher — nhận request từ controller, resolve đúng IProviderStrategy theo integrationKey,
/// delegate việc build URL xuống strategy. Thêm provider mới = thêm strategy + đăng ký DI, không sửa file này.
/// </summary>
public class ConnectionsService : IConnectionsService
{
    private readonly IIntegrationRepository _integrations;
    private readonly IDataProtectionProvider _dataProtection;
    private readonly IDistributedCache _cache;
    private readonly IReadOnlyDictionary<string, IProviderStrategy> _strategies;
    private readonly IConfiguration _config;

    public ConnectionsService(
        IIntegrationRepository integrations,
        IDataProtectionProvider dataProtection,
        IDistributedCache cache,
        IEnumerable<IProviderStrategy> strategies,
        IConfiguration config)
    {
        _integrations = integrations;
        _dataProtection = dataProtection;
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

        // TODO: xóa env fallback và bỏ comment block bên dưới khi chạy production.
        // DEV: đọc plaintext từ appsettings.Development.json để test nhanh không cần encrypt DB.
        var clientId = _config[$"Dev:{integrationKey}:ClientId"]
            ?? throw new BusinessRuleException($"Chưa cấu hình ClientId cho '{integrationKey}'");

        // PRODUCTION: decrypt từ DB thay cho env ở trên.
        // var protector = _dataProtection.CreateProtector("OAuthCredentials");
        // var clientId = protector.Unprotect(integration.ClientIdEncrypted);

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

    public async Task SetCredentialsAsync(
        string integrationKey,
        string clientId,
        string clientSecret,
        CancellationToken ct = default)
    {
        var integration = await _integrations.GetByKeyAsync(integrationKey, ct)
            ?? throw new NotFoundException($"Integration '{integrationKey}' không tồn tại");

        var protector = _dataProtection.CreateProtector("OAuthCredentials");
        integration.ClientIdEncrypted = protector.Protect(clientId);
        integration.ClientSecretEncrypted = protector.Protect(clientSecret);

        _integrations.Update(integration);
        await _integrations.SaveChangesAsync(ct);
    }
}
