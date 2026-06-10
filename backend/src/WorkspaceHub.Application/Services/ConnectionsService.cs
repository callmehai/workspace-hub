using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.OAuth;
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
    private readonly IDataProtectionProvider _dataProtection;
    private readonly IDistributedCache _cache;
    private readonly IReadOnlyDictionary<string, IProviderStrategy> _strategies;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;

    public ConnectionsService(
        IIntegrationRepository integrations,
        IOAuthConnectionRepository oauthConnections,
        IDataProtectionProvider dataProtection,
        IDistributedCache cache,
        IEnumerable<IProviderStrategy> strategies,
        IConfiguration config,
        IHttpClientFactory httpClientFactory)
    {
        _integrations = integrations;
        _oauthConnections = oauthConnections;
        _dataProtection = dataProtection;
        _cache = cache;
        _strategies = strategies.ToDictionary(s => s.ProviderKey, StringComparer.OrdinalIgnoreCase);
        _config = config;
        _httpClientFactory = httpClientFactory;
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

        var integrationKey = payload.IntegrationKey;

        // Step 2 — Load Integration + decrypt credentials.
        var integration = await _integrations.GetByKeyAsync(integrationKey, ct)
            ?? throw new NotFoundException($"Integration '{integrationKey}' không tồn tại");

        // DEV: đọc plaintext từ config; PROD: bỏ comment block bên dưới.
        var clientId     = _config[$"Dev:{integrationKey}:ClientId"]
            ?? throw new BusinessRuleException($"Chưa cấu hình ClientId cho '{integrationKey}'");
        var clientSecret = _config[$"Dev:{integrationKey}:ClientSecret"]
            ?? throw new BusinessRuleException($"Chưa cấu hình ClientSecret cho '{integrationKey}'");

        // PRODUCTION:
        // var protector    = _dataProtection.CreateProtector("OAuthCredentials");
        // var clientId     = protector.Unprotect(integration.ClientIdEncrypted);
        // var clientSecret = protector.Unprotect(integration.ClientSecretEncrypted);

        var redirectUri = _config["Google:RedirectUri"]
            ?? throw new BusinessRuleException("Chưa cấu hình Google:RedirectUri");

        // Step 3 — Exchange code → token.
        var http = _httpClientFactory.CreateClient("GoogleToken");
        var formData = new Dictionary<string, string>
        {
            ["code"]          = code,
            ["client_id"]     = clientId,
            ["client_secret"] = clientSecret,
            ["redirect_uri"]  = redirectUri,
            ["grant_type"]    = "authorization_code"
        };

        var tokenResponse = await http.PostAsync(
            integration.TokenEndpoint,
            new FormUrlEncodedContent(formData), ct);

        if (!tokenResponse.IsSuccessStatusCode)
            throw new BusinessRuleException("Google từ chối code");

        var googleToken = await tokenResponse.Content.ReadFromJsonAsync<GoogleTokenResponse>(ct)
            ?? throw new BusinessRuleException("Google từ chối code");

        // Step 4 — Extract ProviderAccountId từ id_token.
        // TODO: bỏ comment dòng dưới khi test thật với Google account.
        // var providerAccountId = IdTokenParser.ExtractProviderAccountId(googleToken.IdToken);
        var providerAccountId = googleToken.IdToken is not null
            ? IdTokenParser.ExtractProviderAccountId(googleToken.IdToken)
            : "dev-placeholder@gmail.com";

        // Step 5 — Check duplicate.
        var existing = await _oauthConnections.GetByUniqueKeyAsync(
            userId, integration.Id, providerAccountId, ct);
        if (existing is not null)
            throw new ConflictException("Tài khoản Google này đã được kết nối");

        // Step 6 — Encrypt tokens.
        var protector = _dataProtection.CreateProtector("OAuthCredentials");
        var accessTokenEncrypted  = protector.Protect(googleToken.AccessToken);
        var refreshTokenEncrypted = protector.Protect(googleToken.RefreshToken ?? string.Empty);

        // Step 7 — Tạo OAuthConnection.
        var connection = new OAuthConnection
        {
            Id                    = Guid.NewGuid(),
            UserId                = userId,
            IntegrationId         = integration.Id,
            ProviderAccountId     = providerAccountId,
            AccessTokenEncrypted  = accessTokenEncrypted,
            RefreshTokenEncrypted = refreshTokenEncrypted,
            ExpiresAt             = DateTime.UtcNow.AddSeconds(googleToken.ExpiresIn),
            Scopes                = googleToken.Scope,
            Status                = ConnectionStatus.Active,
            LastRefreshedAt       = null
        };

        // Step 8 — Populate ServiceConnections theo scope thực tế Google cấp.
        ServiceConnectionSync.ApplyGrantedScopes(connection, googleToken.Scope);

        // Step 9 — Lưu DB.
        await _oauthConnections.AddAsync(connection, ct);
        await _oauthConnections.SaveChangesAsync(ct);

        var services = connection.ServiceConnections
            .Select(sc => new ServiceConnectionResult(sc.Id, sc.ServiceType.ToString(), sc.IsEnabled))
            .ToList();

        return new CompleteConnectionResult(
            connection.Id,
            integrationKey,
            providerAccountId,
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

        var protector = _dataProtection.CreateProtector("OAuthCredentials");
        integration.ClientIdEncrypted = protector.Protect(clientId);
        integration.ClientSecretEncrypted = protector.Protect(clientSecret);

        _integrations.Update(integration);
        await _integrations.SaveChangesAsync(ct);
    }
}
