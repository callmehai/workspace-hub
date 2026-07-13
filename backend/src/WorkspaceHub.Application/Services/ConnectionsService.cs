using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    private readonly IItemRepository _items;
    private readonly ITokenProtector _tokenProtector;
    private readonly IDistributedCache _cache;
    private readonly IReadOnlyDictionary<string, IProviderStrategy> _strategies;
    private readonly IConfiguration _config;
    private readonly IOAuthTokenClient _tokenClient;
    private readonly IScheduledEmailRepository _scheduledEmails;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<ConnectionsService> _logger;

    public ConnectionsService(
        IIntegrationRepository integrations,
        IConnectionRepository connections,
        IItemRepository items,
        ITokenProtector tokenProtector,
        IDistributedCache cache,
        IEnumerable<IProviderStrategy> strategies,
        IConfiguration config,
        IOAuthTokenClient tokenClient,
        IScheduledEmailRepository scheduledEmails,
        IServiceScopeFactory scopeFactory,
        IMemoryCache memoryCache,
        ILogger<ConnectionsService> logger)
    {
        _integrations = integrations;
        _connections = connections;
        _items = items;
        _tokenProtector = tokenProtector;
        _cache = cache;
        _strategies = strategies.ToDictionary(s => s.ProviderKey, StringComparer.OrdinalIgnoreCase);
        _config = config;
        _tokenClient = tokenClient;
        _scheduledEmails = scheduledEmails;
        _scopeFactory = scopeFactory;
        _memoryCache = memoryCache;
        _logger = logger;
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
            throw new BusinessRuleException("integrations.connectDisabled");

        if (!_strategies.TryGetValue(integrationKey, out var strategy))
            throw new BusinessRuleException($"Provider '{integrationKey}' chưa được hỗ trợ");

        var clientId = _config[$"OAuth:{integrationKey}:ClientId"];
        if (string.IsNullOrEmpty(clientId))
            throw new BusinessRuleException($"Chưa cấu hình ClientId cho '{integrationKey}'");

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

    public async Task<CompleteConnectionResponse> CompleteConnectionAsync(
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

        var clientId = _config[$"OAuth:{integrationKey}:ClientId"];
        if (string.IsNullOrEmpty(clientId))
            throw new BusinessRuleException($"Chưa cấu hình ClientId cho '{integrationKey}'");
        var clientSecret = _config[$"OAuth:{integrationKey}:ClientSecret"];
        if (string.IsNullOrEmpty(clientSecret))
            throw new BusinessRuleException($"Chưa cấu hình ClientSecret cho '{integrationKey}'");

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
        var results = new List<ConnectionItem>();
        foreach (var svcType in tokenResult.GrantedServices)
        {
            var existing = await _connections.GetByUniqueKeyAsync(
                userId, provider, svcType, tokenResult.ProviderAccountId, ct);

            if (existing is not null)
                throw new ConflictException(
                    $"Bạn đã kết nối {svcType} với tài khoản '{tokenResult.ProviderAccountId}' rồi. Hãy ngắt kết nối trước.");

            var connection = new Connection
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                IntegrationId = integration.Id,
                Provider = provider,
                ServiceType = svcType,
                ProviderAccountId = tokenResult.ProviderAccountId,
                AccessTokenEncrypted = accessTokenEncrypted,
                RefreshTokenEncrypted = refreshTokenEncrypted,
                ExpiresAt = expiresAt,
                Status = ConnectionStatus.Active,
                CursorValue = null,
                CreatedAt = DateTime.UtcNow
            };
            await _connections.AddAsync(connection, ct);

            results.Add(new ConnectionItem(
                connection.Id, connection.ServiceType.ToString(), connection.Status.ToString()));
        }

        // Bước 7 — Lưu tất cả vào DB.
        await _connections.SaveChangesAsync(ct);

        return new CompleteConnectionResponse(integrationKey, tokenResult.ProviderAccountId, results);
    }

    public async Task<IReadOnlyList<IntegrationResponse>> GetIntegrationsAsync(CancellationToken ct = default)
    {
        var integrations = await _integrations.ListAsync(ct);
        return integrations
            .OrderBy(i => i.DisplayName)
            .Select(i => new IntegrationResponse(i.Id, i.Key, i.DisplayName, i.IsEnabled))
            .ToList()
            .AsReadOnly();
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

    // ───────────── SCRUM-14: List / Disconnect / Refresh ─────────────

    public async Task<IReadOnlyList<ConnectionDto>> GetConnectionsAsync(Guid userId, CancellationToken ct = default)
    {
        var connections = await _connections.GetByUserIdAsync(userId, ct);
        return connections.Select(MapToConnectionDto).ToList().AsReadOnly();
    }

    public async Task DisconnectAsync(Guid connectionId, Guid userId, CancellationToken ct = default)
    {
        var connection = await _connections.GetByIdTrackedAsync(connectionId, ct)
            ?? throw new NotFoundException("Connection", connectionId);

        if (connection.UserId != userId)
            throw new ForbiddenException("You do not have permission to disconnect this connection.");

        // FK NoAction ở DB → phải xử lý ở service layer trước khi xoá Connection.
        // Dùng explicit transaction để bọc cả ExecuteUpdate/DeleteAsync (SQL trực tiếp) và change tracker lại.

        await _connections.ExecuteInTransactionAsync(async () =>
        {
            // (1) Items.ConnectionId: Delete all items and associations (direct SQL)
            await _items.DeleteByConnectionIdAsync(connectionId, ct);

            // (2) Xoá ScheduledEmails theo ConnectionId (direct SQL)
            await _scheduledEmails.DeleteByConnectionIdAsync(connectionId, ct);

            // (3) Xoá Connection (tracked entity → SaveChanges)
            _connections.Remove(connection);
            await _connections.SaveChangesAsync(ct);
        }, ct);
    }

    public async Task<RefreshConnectionResponse> RefreshConnectionAsync(
        Guid connectionId, Guid userId, CancellationToken ct = default)
    {
        var connection = await _connections.GetByIdTrackedAsync(connectionId, ct)
            ?? throw new NotFoundException("Connection", connectionId);

        if (connection.UserId != userId)
            throw new ForbiddenException("You do not have permission to refresh this connection.");

        // Lấy refresh token đã mã hoá → giải mã
        if (string.IsNullOrEmpty(connection.RefreshTokenEncrypted))
        {
            connection.Status = ConnectionStatus.Error;
            connection.LastError = "No refresh token available";
            _connections.Update(connection);
            await _connections.SaveChangesAsync(ct);
            throw new BusinessRuleException("Refresh token is not available. Please reconnect.");
        }

        string refreshToken;
        try
        {
            refreshToken = _tokenProtector.Unprotect(connection.RefreshTokenEncrypted);
        }
        catch
        {
            connection.Status = ConnectionStatus.Error;
            connection.LastError = "Failed to decrypt refresh token";
            _connections.Update(connection);
            await _connections.SaveChangesAsync(ct);
            throw new BusinessRuleException("Refresh token is invalid. Please reconnect.");
        }

        // Lấy integration để biết token endpoint
        var integration = await _integrations.GetByIdAsync(connection.IntegrationId, ct)
            ?? throw new NotFoundException("Integration", connection.IntegrationId);

        var clientId = _config[$"OAuth:{integration.Key}:ClientId"];
        if (string.IsNullOrEmpty(clientId))
            throw new BusinessRuleException($"Chưa cấu hình ClientId cho '{integration.Key}'");
        var clientSecret = _config[$"OAuth:{integration.Key}:ClientSecret"];
        if (string.IsNullOrEmpty(clientSecret))
            throw new BusinessRuleException($"Chưa cấu hình ClientSecret cho '{integration.Key}'");

        // Gọi provider để refresh token
        try
        {
            var formData = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret
            };

            var responseJson = await _tokenClient.PostFormAsync(integration.TokenEndpoint, formData, ct);
            var tokenResponse = JsonSerializer.Deserialize<JsonElement>(responseJson);

            // Parse access_token và expires_in từ response
            if (!tokenResponse.TryGetProperty("access_token", out var accessTokenEl))
            {
                // Provider trả lỗi → set Status = Error, throw 422
                connection.Status = ConnectionStatus.Error;
                connection.LastError = "Provider did not return a new access token";
                _connections.Update(connection);
                await _connections.SaveChangesAsync(ct);
                throw new BusinessRuleException("Refresh token is invalid. Please reconnect.");
            }

            var newAccessToken = accessTokenEl.GetString()!;
            var expiresIn = tokenResponse.TryGetProperty("expires_in", out var expiresInEl)
                ? expiresInEl.GetInt32()
                : 3600;

            // Cập nhật connection
            connection.AccessTokenEncrypted = _tokenProtector.Protect(newAccessToken);
            connection.ExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn);
            connection.Status = ConnectionStatus.Active;
            connection.LastError = null;

            // Nếu provider trả refresh_token mới (rotation), cập nhật luôn
            if (tokenResponse.TryGetProperty("refresh_token", out var newRefreshEl))
            {
                var newRefresh = newRefreshEl.GetString();
                if (!string.IsNullOrEmpty(newRefresh))
                {
                    connection.RefreshTokenEncrypted = _tokenProtector.Protect(newRefresh);
                }
            }

            _connections.Update(connection);
            await _connections.SaveChangesAsync(ct);

            return new RefreshConnectionResponse
            {
                ConnectionId = connection.Id,
                ExpiresAt = connection.ExpiresAt,
                Status = connection.Status.ToString()
            };
        }
        catch (BusinessRuleException)
        {
            throw; // re-throw our own exceptions
        }
        catch (Exception ex)
        {
            // Provider lỗi → set Status = Error, throw 422
            connection.Status = ConnectionStatus.Error;
            connection.LastError = ex.Message;
            _connections.Update(connection);
            await _connections.SaveChangesAsync(ct);
            throw new BusinessRuleException("Refresh token is invalid. Please reconnect.");
        }
    }

    // ───────────── Helpers ─────────────

    // Global recent (chủ yếu INBOX + thư mới). FullSync còn list bổ sung từng mailbox
    // (SENT/DRAFT/STARRED/CATEGORY_*) nên tổng thực tế cao hơn — xem GmailSyncService.
    private const int GmailDefaultBatchSize = 100;

    public async Task<ManualSyncResult> TriggerManualSyncAsync(Guid connectionId, Guid userId, CancellationToken ct = default)
    {
        var conn = await _connections.GetByIdTrackedAsync(connectionId, ct);
        if (conn == null) throw new NotFoundException("Connection", connectionId);
        if (conn.UserId != userId) throw new ForbiddenException("You do not have permission to sync this connection.");

        var cacheKey = $"manual_sync_throttle_{connectionId}";
        if (_memoryCache.TryGetValue(cacheKey, out _))
        {
            return new ManualSyncResult(429);
        }

        _memoryCache.Set(cacheKey, true, TimeSpan.FromSeconds(60));

        var jobId = Guid.NewGuid();

        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var bgRepo = scope.ServiceProvider.GetRequiredService<IConnectionRepository>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<ConnectionsService>>();

            try
            {
                var scopedConn = await bgRepo.GetByIdTrackedAsync(connectionId, CancellationToken.None);
                if (scopedConn == null) return;

                switch (scopedConn.ServiceType)
                {
                    case ServiceType.Gmail:
                        var gmailSync = scope.ServiceProvider.GetRequiredService<IGmailSyncService>();
                        await gmailSync.SyncConnectionAsync(scopedConn, GmailDefaultBatchSize, CancellationToken.None);
                        break;
                    case ServiceType.GCal:
                        var calendarSync = scope.ServiceProvider.GetRequiredService<ICalendarSyncService>();
                        await calendarSync.SyncConnectionAsync(scopedConn, CancellationToken.None);
                        break;
                    case ServiceType.Drive:
                        var driveSync = scope.ServiceProvider.GetRequiredService<IDriveSyncService>();
                        await driveSync.SyncConnectionAsync(scopedConn, CancellationToken.None);
                        break;
                    default:
                        logger.LogWarning("Manual sync skipped: ServiceType {ServiceType} not supported.", scopedConn.ServiceType);
                        break;
                }
            }
            catch (Exception ex)
            {
                // GoogleApiException từ provider (403/429/5xx) sẽ bị bắt ở đây —
                // ghi vào LastError + đánh Status=Error để UI hiển thị trạng thái lỗi.
                logger.LogError(ex, "Manual sync failed for connection {ConnectionId}", connectionId);

                var connToUpdate = await bgRepo.GetByIdTrackedAsync(connectionId, CancellationToken.None);
                if (connToUpdate != null)
                {
                    connToUpdate.Status = ConnectionStatus.Error;
                    connToUpdate.LastError = ex.Message;
                    await bgRepo.SaveChangesAsync(CancellationToken.None);
                }
            }
        });

        return new ManualSyncResult(202, jobId);
    }

    /// <summary>
    /// Mask token: trả về "********" để giấu ciphertext.
    /// CONVENTIONS.md: "Token response luôn mask"
    /// </summary>
    private static string MaskToken(string _)
    {
        return "********";
    }

    private static ConnectionDto MapToConnectionDto(Connection connection)
    {
        return new ConnectionDto
        {
            Id = connection.Id,
            Provider = connection.Provider.ToString(),
            ServiceType = connection.ServiceType.ToString(),
            ProviderAccountId = connection.ProviderAccountId,
            MaskedToken = MaskToken(connection.AccessTokenEncrypted),
            Status = connection.Status.ToString(),
            ExpiresAt = connection.ExpiresAt,
            LastSyncedAt = connection.LastSyncedAt,
            CreatedAt = connection.CreatedAt
        };
    }
}

