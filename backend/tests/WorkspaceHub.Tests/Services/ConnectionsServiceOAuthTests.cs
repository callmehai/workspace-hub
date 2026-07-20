using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.OAuth.Core;
using WorkspaceHub.Application.Security;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests.Services;

/// <summary>
/// Unit test cho các nhánh OAuth / integration / manual-sync của <see cref="ConnectionsService"/>:
/// InitiateConnectionAsync, CompleteConnectionAsync, GetIntegrationsAsync, ToggleIntegrationAsync,
/// TriggerManualSyncAsync.
///
/// (List / Disconnect / Refresh đã có ở <c>tests/WorkspaceHub.Tests/ConnectionsServiceTests.cs</c>.)
///
/// Dùng cache THẬT (MemoryDistributedCache + MemoryCache) thay vì mock, vì service gọi qua
/// extension method (GetStringAsync/SetStringAsync/Set) — mock interface rất dễ lệch hành vi.
/// </summary>
public class ConnectionsServiceOAuthTests
{
    private const string IntegrationKey = "google";
    private const string RedirectUri = "https://app.workspace-hub.space/oauth/callback";

    private readonly Mock<IIntegrationRepository> _integrationsMock = new();
    private readonly Mock<IConnectionRepository> _connectionsMock = new();
    private readonly Mock<IItemRepository> _itemsMock = new();
    private readonly Mock<ITokenProtector> _tokenProtectorMock = new();
    private readonly Mock<IOAuthTokenClient> _tokenClientMock = new();
    private readonly Mock<IScheduledEmailRepository> _scheduledEmailsMock = new();
    private readonly Mock<IProviderStrategy> _strategyMock = new();

    // Scope factory + service provider cho background task của TriggerManualSyncAsync
    private readonly Mock<IServiceScopeFactory> _scopeFactoryMock = new();
    private readonly Mock<IServiceScope> _scopeMock = new();
    private readonly Mock<IServiceProvider> _scopedProviderMock = new();
    private readonly Mock<IGmailSyncService> _gmailSyncMock = new();
    private readonly Mock<ICalendarSyncService> _calendarSyncMock = new();
    private readonly Mock<IDriveSyncService> _driveSyncMock = new();

    private readonly Mock<ILogger<ConnectionsService>> _loggerMock = new();

    private readonly IDistributedCache _cache =
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
    private readonly IMemoryCache _memoryCache = new MemoryCache(new MemoryCacheOptions());

    private readonly ConnectionsService _sut;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _otherUserId = Guid.NewGuid();

    public ConnectionsServiceOAuthTests()
    {
        // ProviderKey phải set TRƯỚC khi dựng service — ctor build dictionary strategy ngay lúc đó.
        _strategyMock.SetupGet(s => s.ProviderKey).Returns(IntegrationKey);

        // Background task của TriggerManualSyncAsync resolve service từ scope riêng.
        _scopedProviderMock.Setup(p => p.GetService(typeof(IConnectionRepository))).Returns(_connectionsMock.Object);
        _scopedProviderMock.Setup(p => p.GetService(typeof(ILogger<ConnectionsService>))).Returns(_loggerMock.Object);
        _scopedProviderMock.Setup(p => p.GetService(typeof(IGmailSyncService))).Returns(_gmailSyncMock.Object);
        _scopedProviderMock.Setup(p => p.GetService(typeof(ICalendarSyncService))).Returns(_calendarSyncMock.Object);
        _scopedProviderMock.Setup(p => p.GetService(typeof(IDriveSyncService))).Returns(_driveSyncMock.Object);
        _scopeMock.SetupGet(s => s.ServiceProvider).Returns(_scopedProviderMock.Object);
        _scopeFactoryMock.Setup(f => f.CreateScope()).Returns(_scopeMock.Object);

        // Mặc định user chưa có connection nào (CompleteConnectionAsync đọc list này để build
        // ExistingActiveProviderAccountIds) — tránh null reference từ default value của Moq.
        _connectionsMock
            .Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Connection>().AsReadOnly());
        _connectionsMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _integrationsMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _sut = CreateSut();
    }

    // ───────────── Helpers ─────────────

    private ConnectionsService CreateSut(
        IConfiguration? config = null,
        IEnumerable<IProviderStrategy>? strategies = null)
        => new(
            _integrationsMock.Object,
            _connectionsMock.Object,
            _itemsMock.Object,
            _tokenProtectorMock.Object,
            _cache,
            strategies ?? new[] { _strategyMock.Object },
            config ?? CreateConfig(),
            _tokenClientMock.Object,
            _scheduledEmailsMock.Object,
            _scopeFactoryMock.Object,
            _memoryCache,
            _loggerMock.Object);

    private static IConfiguration CreateConfig(
        string? clientId = "google-client-id",
        string? clientSecret = "google-client-secret")
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"OAuth:{IntegrationKey}:ClientId"] = clientId,
                [$"OAuth:{IntegrationKey}:ClientSecret"] = clientSecret
            })
            .Build();

    private static Integration CreateIntegration(string key = IntegrationKey, bool isEnabled = true) => new()
    {
        Id = Guid.NewGuid(),
        Key = key,
        DisplayName = "Google Workspace",
        IconUrl = "https://google.com/favicon.ico",
        Description = "Gmail · Calendar · Drive",
        Provider = nameof(ProviderType.Google),
        AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth",
        TokenEndpoint = "https://oauth2.googleapis.com/token",
        SupportedServices = "[\"Gmail\",\"GCal\",\"Drive\"]",
        IsEnabled = isEnabled
    };

    private Connection CreateConnection(
        Guid? userId = null,
        ServiceType serviceType = ServiceType.Gmail,
        ConnectionStatus status = ConnectionStatus.Active)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId ?? _userId,
            IntegrationId = Guid.NewGuid(),
            Provider = ProviderType.Google,
            ServiceType = serviceType,
            ProviderAccountId = "user@gmail.com",
            AccessTokenEncrypted = "old-encrypted-access",
            RefreshTokenEncrypted = "old-encrypted-refresh",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-5),
            Status = status,
            CursorValue = "cursor-123",
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };

    private static TokenExchangeResult CreateTokenResult(
        string accessToken = "plain-access-token",
        string? refreshToken = "plain-refresh-token",
        int expiresIn = 3600,
        string providerAccountId = "user@gmail.com",
        ServiceType[]? granted = null)
        => new(
            accessToken,
            refreshToken,
            expiresIn,
            "https://www.googleapis.com/auth/gmail.modify",
            providerAccountId,
            granted is null || granted.Length == 0 ? new[] { ServiceType.Gmail } : granted);

    /// <summary>Ghi sẵn state payload vào cache như InitiateConnectionAsync đã làm.</summary>
    private async Task<string> SeedStateAsync(
        Guid? userId = null,
        string integrationKey = IntegrationKey,
        string serviceType = "Gmail")
    {
        var state = Guid.NewGuid().ToString("N");
        var payload = JsonSerializer.Serialize(
            new OAuthStatePayload(integrationKey, userId ?? _userId, RedirectUri, serviceType));
        await _cache.SetStringAsync($"oauth:state:{state}", payload);
        return state;
    }

    /// <summary>Chuẩn bị đường đi thành công của CompleteConnectionAsync.</summary>
    private Integration SetupCompleteHappyPath(TokenExchangeResult tokenResult)
    {
        var integration = CreateIntegration();
        _integrationsMock
            .Setup(r => r.GetByKeyAsync(IntegrationKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(integration);
        _strategyMock
            .Setup(s => s.ExchangeCodeAsync(It.IsAny<ExchangeCodeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenResult);
        _tokenProtectorMock.Setup(p => p.Protect(It.IsAny<string>()))
            .Returns<string>(plain => $"enc({plain})");
        return integration;
    }

    /// <summary>Chờ tín hiệu từ background task (fire-and-forget Task.Run) tối đa 5 giây.</summary>
    private static async Task<bool> WaitAsync(TaskCompletionSource<bool> signal)
    {
        var finished = await Task.WhenAny(signal.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        return finished == signal.Task;
    }

    // ── InitiateConnectionAsync ─────────────────────────────────

    [Fact]
    public async Task InitiateConnectionAsync_ThrowsBusinessRule_WhenServiceTypeInvalid()
    {
        // Act & Assert — serviceType không nằm trong enum → 422, chưa đụng tới repo
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _sut.InitiateConnectionAsync(IntegrationKey, "Slack", RedirectUri, _userId));

        ex.Message.Should().Contain("Slack");
        _integrationsMock.Verify(
            r => r.GetByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InitiateConnectionAsync_ThrowsNotFound_WhenIntegrationDoesNotExist()
    {
        // Arrange
        _integrationsMock
            .Setup(r => r.GetByKeyAsync("unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Integration?)null);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(
            () => _sut.InitiateConnectionAsync("unknown", "Gmail", RedirectUri, _userId));
    }

    [Fact]
    public async Task InitiateConnectionAsync_ThrowsBusinessRule_WhenIntegrationDisabled()
    {
        // Arrange — admin tắt integration
        _integrationsMock
            .Setup(r => r.GetByKeyAsync(IntegrationKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateIntegration(isEnabled: false));

        // Act & Assert — mã lỗi i18n cho FE
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _sut.InitiateConnectionAsync(IntegrationKey, "Gmail", RedirectUri, _userId));

        ex.Message.Should().Be("integrations.connectDisabled");
        _strategyMock.Verify(
            s => s.BuildAuthUrlAsync(It.IsAny<BuildAuthUrlRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InitiateConnectionAsync_ThrowsBusinessRule_WhenProviderStrategyMissing()
    {
        // Arrange — integration có trong DB nhưng chưa có strategy tương ứng trong code
        _integrationsMock
            .Setup(r => r.GetByKeyAsync(IntegrationKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateIntegration());
        var sut = CreateSut(strategies: Enumerable.Empty<IProviderStrategy>());

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => sut.InitiateConnectionAsync(IntegrationKey, "Gmail", RedirectUri, _userId));

        ex.Message.Should().Contain("chưa được hỗ trợ");
    }

    [Fact]
    public async Task InitiateConnectionAsync_ThrowsBusinessRule_WhenClientIdNotConfigured()
    {
        // Arrange — thiếu OAuth:google:ClientId trong config
        _integrationsMock
            .Setup(r => r.GetByKeyAsync(IntegrationKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateIntegration());
        var sut = CreateSut(config: CreateConfig(clientId: null));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => sut.InitiateConnectionAsync(IntegrationKey, "Gmail", RedirectUri, _userId));

        ex.Message.Should().Contain("ClientId");
    }

    [Fact]
    public async Task InitiateConnectionAsync_ReturnsAuthorizationUrlAndState_OnSuccess()
    {
        // Arrange
        var integration = CreateIntegration();
        _integrationsMock
            .Setup(r => r.GetByKeyAsync(IntegrationKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(integration);

        BuildAuthUrlRequest? captured = null;
        var strategyResult = new InitiateConnectionResult(
            "https://accounts.google.com/o/oauth2/v2/auth?client_id=google-client-id", "state-tu-strategy");
        _strategyMock
            .Setup(s => s.BuildAuthUrlAsync(It.IsAny<BuildAuthUrlRequest>(), It.IsAny<CancellationToken>()))
            .Callback<BuildAuthUrlRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(strategyResult);

        // Act
        var result = await _sut.InitiateConnectionAsync(IntegrationKey, "Gmail", RedirectUri, _userId);

        // Assert — service trả nguyên kết quả của strategy
        result.AuthorizationUrl.Should().Be(strategyResult.AuthorizationUrl);
        result.State.Should().Be(strategyResult.State);

        // Assert — service truyền đúng dữ liệu xuống strategy (state do service sinh, 32 ký tự hex)
        captured.Should().NotBeNull();
        captured!.ClientId.Should().Be("google-client-id");
        captured.RedirectUri.Should().Be(RedirectUri);
        captured.ServiceType.Should().Be("Gmail");
        captured.Integration.Should().BeSameAs(integration);
        captured.State.Should().NotBeNullOrWhiteSpace();
        captured.State.Should().HaveLength(32);
    }

    [Fact]
    public async Task InitiateConnectionAsync_StoresStatePayloadInCache_OnSuccess()
    {
        // Arrange
        _integrationsMock
            .Setup(r => r.GetByKeyAsync(IntegrationKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateIntegration());
        BuildAuthUrlRequest? captured = null;
        _strategyMock
            .Setup(s => s.BuildAuthUrlAsync(It.IsAny<BuildAuthUrlRequest>(), It.IsAny<CancellationToken>()))
            .Callback<BuildAuthUrlRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new InitiateConnectionResult("https://accounts.google.com/o/oauth2/v2/auth", "state"));

        // Act
        await _sut.InitiateConnectionAsync(IntegrationKey, "GCal", RedirectUri, _userId);

        // Assert — state được lưu cache kèm đủ thông tin để callback xác minh
        captured.Should().NotBeNull();
        var cached = await _cache.GetStringAsync($"oauth:state:{captured!.State}");
        cached.Should().NotBeNull();

        var payload = JsonSerializer.Deserialize<OAuthStatePayload>(cached!);
        payload.Should().NotBeNull();
        payload!.IntegrationKey.Should().Be(IntegrationKey);
        payload.UserId.Should().Be(_userId);
        payload.RedirectUri.Should().Be(RedirectUri);
        payload.ServiceType.Should().Be("GCal");
    }

    // ── CompleteConnectionAsync ─────────────────────────────────

    [Fact]
    public async Task CompleteConnectionAsync_ThrowsCsrf_WhenStateNotFoundOrExpired()
    {
        // Act & Assert — state chưa từng lưu (hoặc đã hết hạn 10 phút)
        await Assert.ThrowsAsync<CsrfException>(
            () => _sut.CompleteConnectionAsync("auth-code", "state-khong-ton-tai", _userId));

        _strategyMock.Verify(
            s => s.ExchangeCodeAsync(It.IsAny<ExchangeCodeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CompleteConnectionAsync_ThrowsCsrf_WhenStateBelongsToAnotherUser()
    {
        // Arrange — state do user khác tạo
        var state = await SeedStateAsync(userId: _otherUserId);

        // Act & Assert
        await Assert.ThrowsAsync<CsrfException>(
            () => _sut.CompleteConnectionAsync("auth-code", state, _userId));
    }

    [Fact]
    public async Task CompleteConnectionAsync_RemovesState_SoItCannotBeReplayed()
    {
        // Arrange
        var state = await SeedStateAsync();
        SetupCompleteHappyPath(CreateTokenResult());

        // Act — lần 1 thành công
        await _sut.CompleteConnectionAsync("auth-code", state, _userId);

        // Assert — state đã bị xoá khỏi cache → replay lần 2 bị chặn
        (await _cache.GetStringAsync($"oauth:state:{state}")).Should().BeNull();
        await Assert.ThrowsAsync<CsrfException>(
            () => _sut.CompleteConnectionAsync("auth-code", state, _userId));
    }

    [Fact]
    public async Task CompleteConnectionAsync_ThrowsNotFound_WhenIntegrationDoesNotExist()
    {
        // Arrange — state hợp lệ nhưng integration đã bị xoá khỏi catalog
        var state = await SeedStateAsync();
        _integrationsMock
            .Setup(r => r.GetByKeyAsync(IntegrationKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Integration?)null);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(
            () => _sut.CompleteConnectionAsync("auth-code", state, _userId));
    }

    [Fact]
    public async Task CompleteConnectionAsync_ThrowsBusinessRule_WhenClientSecretNotConfigured()
    {
        // Arrange
        var state = await SeedStateAsync();
        _integrationsMock
            .Setup(r => r.GetByKeyAsync(IntegrationKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateIntegration());
        var sut = CreateSut(config: CreateConfig(clientSecret: null));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => sut.CompleteConnectionAsync("auth-code", state, _userId));

        ex.Message.Should().Contain("ClientSecret");
    }

    [Fact]
    public async Task CompleteConnectionAsync_ThrowsBusinessRule_WhenNoServiceGranted()
    {
        // Arrange — user bỏ tick toàn bộ scope ở màn hình consent
        var state = await SeedStateAsync();
        var integration = CreateIntegration();
        _integrationsMock
            .Setup(r => r.GetByKeyAsync(IntegrationKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(integration);
        _strategyMock
            .Setup(s => s.ExchangeCodeAsync(It.IsAny<ExchangeCodeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenExchangeResult("access", "refresh", 3600, "", "user@gmail.com", Array.Empty<ServiceType>()));

        // Act & Assert
        await Assert.ThrowsAsync<BusinessRuleException>(
            () => _sut.CompleteConnectionAsync("auth-code", state, _userId));

        _connectionsMock.Verify(
            r => r.AddAsync(It.IsAny<Connection>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CompleteConnectionAsync_CreatesConnectionWithEncryptedTokens_WhenNoneExists()
    {
        // Arrange
        var state = await SeedStateAsync();
        var integration = SetupCompleteHappyPath(CreateTokenResult());
        _connectionsMock
            .Setup(r => r.GetByUniqueKeyAsync(
                _userId, ProviderType.Google, ServiceType.Gmail, "user@gmail.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Connection?)null);

        Connection? added = null;
        _connectionsMock
            .Setup(r => r.AddAsync(It.IsAny<Connection>(), It.IsAny<CancellationToken>()))
            .Callback<Connection, CancellationToken>((c, _) => added = c)
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.CompleteConnectionAsync("auth-code", state, _userId);

        // Assert — response
        result.IntegrationKey.Should().Be(IntegrationKey);
        result.ProviderAccountId.Should().Be("user@gmail.com");
        result.Connections.Should().ContainSingle();
        result.Connections[0].ServiceType.Should().Be("Gmail");
        result.Connections[0].Status.Should().Be("Active");

        // Assert — entity lưu xuống DB: token đã mã hoá, cursor null (sync lần đầu)
        added.Should().NotBeNull();
        added!.UserId.Should().Be(_userId);
        added.IntegrationId.Should().Be(integration.Id);
        added.Provider.Should().Be(ProviderType.Google);
        added.ServiceType.Should().Be(ServiceType.Gmail);
        added.AccessTokenEncrypted.Should().Be("enc(plain-access-token)");
        added.RefreshTokenEncrypted.Should().Be("enc(plain-refresh-token)");
        added.Status.Should().Be(ConnectionStatus.Active);
        added.CursorValue.Should().BeNull();
        added.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddSeconds(3600), TimeSpan.FromMinutes(1));

        _tokenProtectorMock.Verify(p => p.Protect("plain-access-token"), Times.Once);
        _tokenProtectorMock.Verify(p => p.Protect("plain-refresh-token"), Times.Once);
        _connectionsMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CompleteConnectionAsync_StoresEmptyRefreshToken_WhenProviderReturnsNone()
    {
        // Arrange — provider không trả refresh_token (đã cấp quyền trước đó)
        var state = await SeedStateAsync();
        SetupCompleteHappyPath(CreateTokenResult(refreshToken: null));

        Connection? added = null;
        _connectionsMock
            .Setup(r => r.AddAsync(It.IsAny<Connection>(), It.IsAny<CancellationToken>()))
            .Callback<Connection, CancellationToken>((c, _) => added = c)
            .Returns(Task.CompletedTask);

        // Act
        await _sut.CompleteConnectionAsync("auth-code", state, _userId);

        // Assert — giữ chuỗi rỗng, KHÔNG gọi Protect cho refresh token
        added.Should().NotBeNull();
        added!.RefreshTokenEncrypted.Should().BeEmpty();
        _tokenProtectorMock.Verify(p => p.Protect("plain-access-token"), Times.Once);
        _tokenProtectorMock.Verify(p => p.Protect(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CompleteConnectionAsync_CreatesOneConnectionPerGrantedService()
    {
        // Arrange — mô hình B: mỗi ServiceType 1 row Connection riêng
        var state = await SeedStateAsync();
        SetupCompleteHappyPath(CreateTokenResult(granted: new[] { ServiceType.Gmail, ServiceType.Drive }));

        var added = new List<Connection>();
        _connectionsMock
            .Setup(r => r.AddAsync(It.IsAny<Connection>(), It.IsAny<CancellationToken>()))
            .Callback<Connection, CancellationToken>((c, _) => added.Add(c))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.CompleteConnectionAsync("auth-code", state, _userId);

        // Assert
        added.Should().HaveCount(2);
        added.Select(c => c.ServiceType).Should().BeEquivalentTo(new[] { ServiceType.Gmail, ServiceType.Drive });
        result.Connections.Select(c => c.ServiceType).Should().BeEquivalentTo(new[] { "Gmail", "Drive" });
        // SaveChanges 1 lần cho cả batch
        _connectionsMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CompleteConnectionAsync_UpdatesExistingConnection_WhenSameProviderAccount()
    {
        // Arrange — reconnect: đã có Connection cùng (user, provider, service, account) đang Error
        var state = await SeedStateAsync();
        SetupCompleteHappyPath(CreateTokenResult());

        var existing = CreateConnection(status: ConnectionStatus.Error);
        _connectionsMock
            .Setup(r => r.GetByUniqueKeyAsync(
                _userId, ProviderType.Google, ServiceType.Gmail, "user@gmail.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        // Act
        var result = await _sut.CompleteConnectionAsync("auth-code", state, _userId);

        // Assert — cập nhật tại chỗ, KHÔNG tạo row trùng
        _connectionsMock.Verify(
            r => r.AddAsync(It.IsAny<Connection>(), It.IsAny<CancellationToken>()), Times.Never);
        existing.AccessTokenEncrypted.Should().Be("enc(plain-access-token)");
        existing.RefreshTokenEncrypted.Should().Be("enc(plain-refresh-token)");
        existing.Status.Should().Be(ConnectionStatus.Active);
        existing.CursorValue.Should().Be("cursor-123"); // giữ cursor delta-sync cũ
        result.Connections.Should().ContainSingle();
        result.Connections[0].Id.Should().Be(existing.Id);
        _connectionsMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CompleteConnectionAsync_KeepsOldRefreshToken_WhenProviderReturnsNoneOnReconnect()
    {
        // Arrange — reconnect nhưng provider không trả refresh_token mới
        var state = await SeedStateAsync();
        SetupCompleteHappyPath(CreateTokenResult(refreshToken: null));

        var existing = CreateConnection();
        _connectionsMock
            .Setup(r => r.GetByUniqueKeyAsync(
                _userId, ProviderType.Google, ServiceType.Gmail, "user@gmail.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        // Act
        await _sut.CompleteConnectionAsync("auth-code", state, _userId);

        // Assert — refresh token cũ được giữ nguyên
        existing.RefreshTokenEncrypted.Should().Be("old-encrypted-refresh");
        existing.AccessTokenEncrypted.Should().Be("enc(plain-access-token)");
    }

    [Fact]
    public async Task CompleteConnectionAsync_PassesActiveAccountIdsToStrategy()
    {
        // Arrange — Jira đa site: strategy cần biết account nào đã kết nối để chọn site mới
        var state = await SeedStateAsync();
        SetupCompleteHappyPath(CreateTokenResult());

        var activeConn = CreateConnection();
        var disconnected = CreateConnection(status: ConnectionStatus.Disconnected);
        disconnected.ProviderAccountId = "other@gmail.com";
        _connectionsMock
            .Setup(r => r.GetByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Connection> { activeConn, disconnected }.AsReadOnly());

        ExchangeCodeRequest? captured = null;
        _strategyMock
            .Setup(s => s.ExchangeCodeAsync(It.IsAny<ExchangeCodeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ExchangeCodeRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(CreateTokenResult());

        // Act
        await _sut.CompleteConnectionAsync("auth-code", state, _userId);

        // Assert — chỉ account đang Active mới được truyền xuống
        captured.Should().NotBeNull();
        captured!.Code.Should().Be("auth-code");
        captured.RedirectUri.Should().Be(RedirectUri);
        captured.ClientId.Should().Be("google-client-id");
        captured.ClientSecret.Should().Be("google-client-secret");
        captured.ServiceType.Should().Be("Gmail");
        captured.ExistingActiveProviderAccountIds.Should().BeEquivalentTo(new[] { "user@gmail.com" });
    }

    // ── GetIntegrationsAsync ────────────────────────────────────

    [Fact]
    public async Task GetIntegrationsAsync_ReturnsEmpty_WhenCatalogEmpty()
    {
        // Arrange
        _integrationsMock
            .Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Integration>().AsReadOnly());

        // Act
        var result = await _sut.GetIntegrationsAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetIntegrationsAsync_MapsToResponse_OrderedByDisplayName()
    {
        // Arrange
        var google = CreateIntegration();
        var atlassian = CreateIntegration(key: "atlassian", isEnabled: false);
        atlassian.DisplayName = "Atlassian Jira";
        _integrationsMock
            .Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Integration> { google, atlassian }.AsReadOnly());

        // Act
        var result = await _sut.GetIntegrationsAsync();

        // Assert — sắp xếp theo DisplayName + map đủ 4 field
        result.Should().HaveCount(2);
        result[0].Key.Should().Be("atlassian");
        result[0].Id.Should().Be(atlassian.Id);
        result[0].DisplayName.Should().Be("Atlassian Jira");
        result[0].IsEnabled.Should().BeFalse();
        result[1].Key.Should().Be(IntegrationKey);
        result[1].IsEnabled.Should().BeTrue();
    }

    // ── ToggleIntegrationAsync ──────────────────────────────────

    [Fact]
    public async Task ToggleIntegrationAsync_ThrowsNotFound_WhenKeyDoesNotExist()
    {
        // Arrange
        _integrationsMock
            .Setup(r => r.GetByKeyAsync("unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Integration?)null);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(
            () => _sut.ToggleIntegrationAsync("unknown", true));

        _integrationsMock.Verify(r => r.Update(It.IsAny<Integration>()), Times.Never);
        _integrationsMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ToggleIntegrationAsync_DisablesIntegration_AndPersists()
    {
        // Arrange — admin tắt Google
        var integration = CreateIntegration();
        _integrationsMock
            .Setup(r => r.GetByKeyAsync(IntegrationKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(integration);

        // Act
        var result = await _sut.ToggleIntegrationAsync(IntegrationKey, false);

        // Assert
        integration.IsEnabled.Should().BeFalse();
        result.Id.Should().Be(integration.Id);
        result.Key.Should().Be(IntegrationKey);
        result.DisplayName.Should().Be(integration.DisplayName);
        result.IsEnabled.Should().BeFalse();
        _integrationsMock.Verify(r => r.Update(integration), Times.Once);
        _integrationsMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ToggleIntegrationAsync_EnablesIntegration_AndPersists()
    {
        // Arrange — bật lại integration đang tắt
        var integration = CreateIntegration(key: "atlassian", isEnabled: false);
        _integrationsMock
            .Setup(r => r.GetByKeyAsync("atlassian", It.IsAny<CancellationToken>()))
            .ReturnsAsync(integration);

        // Act
        var result = await _sut.ToggleIntegrationAsync("atlassian", true);

        // Assert
        integration.IsEnabled.Should().BeTrue();
        result.IsEnabled.Should().BeTrue();
        _integrationsMock.Verify(r => r.Update(integration), Times.Once);
        _integrationsMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── TriggerManualSyncAsync ──────────────────────────────────

    [Fact]
    public async Task TriggerManualSyncAsync_ThrowsNotFound_WhenConnectionDoesNotExist()
    {
        // Arrange
        var connectionId = Guid.NewGuid();
        _connectionsMock
            .Setup(r => r.GetByIdTrackedAsync(connectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Connection?)null);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(
            () => _sut.TriggerManualSyncAsync(connectionId, _userId));
    }

    [Fact]
    public async Task TriggerManualSyncAsync_ThrowsForbidden_WhenConnectionBelongsToAnotherUser()
    {
        // Arrange
        var connection = CreateConnection(userId: _otherUserId);
        _connectionsMock
            .Setup(r => r.GetByIdTrackedAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        // Act & Assert
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _sut.TriggerManualSyncAsync(connection.Id, _userId));

        _scopeFactoryMock.Verify(f => f.CreateScope(), Times.Never);
    }

    [Fact]
    public async Task TriggerManualSyncAsync_Returns202WithJobId_OnFirstCall()
    {
        // Arrange
        var connection = CreateConnection();
        _connectionsMock
            .Setup(r => r.GetByIdTrackedAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        // Act
        var result = await _sut.TriggerManualSyncAsync(connection.Id, _userId);

        // Assert — chấp nhận, chạy nền
        result.StatusCode.Should().Be(202);
        result.JobId.HasValue.Should().BeTrue();
        result.JobId!.Value.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task TriggerManualSyncAsync_Returns429_WhenCalledAgainWithinThrottleWindow()
    {
        // Arrange — throttle 60 giây/connection
        var connection = CreateConnection();
        _connectionsMock
            .Setup(r => r.GetByIdTrackedAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        // Act
        var first = await _sut.TriggerManualSyncAsync(connection.Id, _userId);
        var second = await _sut.TriggerManualSyncAsync(connection.Id, _userId);

        // Assert
        first.StatusCode.Should().Be(202);
        second.StatusCode.Should().Be(429);
        second.JobId.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task TriggerManualSyncAsync_ThrottlesPerConnection_NotGlobally()
    {
        // Arrange — 2 connection khác nhau của cùng user
        var gmail = CreateConnection();
        var drive = CreateConnection(serviceType: ServiceType.Drive);
        _connectionsMock
            .Setup(r => r.GetByIdTrackedAsync(gmail.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(gmail);
        _connectionsMock
            .Setup(r => r.GetByIdTrackedAsync(drive.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(drive);

        // Act
        var first = await _sut.TriggerManualSyncAsync(gmail.Id, _userId);
        var second = await _sut.TriggerManualSyncAsync(drive.Id, _userId);

        // Assert
        first.StatusCode.Should().Be(202);
        second.StatusCode.Should().Be(202);
    }

    [Fact]
    public async Task TriggerManualSyncAsync_RunsGmailSyncInBackground_ForGmailConnection()
    {
        // Arrange
        var connection = CreateConnection(serviceType: ServiceType.Gmail);
        _connectionsMock
            .Setup(r => r.GetByIdTrackedAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        var signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _gmailSyncMock
            .Setup(s => s.SyncConnectionAsync(connection, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback(() => signal.TrySetResult(true))
            .ReturnsAsync(new SyncResult(10, 5, 0, "cursor-new"));

        // Act
        var result = await _sut.TriggerManualSyncAsync(connection.Id, _userId);

        // Assert — background task gọi đúng Gmail sync (batch mặc định 100)
        result.StatusCode.Should().Be(202);
        (await WaitAsync(signal)).Should().BeTrue("background task phải gọi Gmail sync");
        _gmailSyncMock.Verify(
            s => s.SyncConnectionAsync(connection, 100, It.IsAny<CancellationToken>()), Times.Once);
        _calendarSyncMock.Verify(
            s => s.SyncConnectionAsync(It.IsAny<Connection>(), It.IsAny<CancellationToken>()), Times.Never);
        _driveSyncMock.Verify(
            s => s.SyncConnectionAsync(It.IsAny<Connection>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TriggerManualSyncAsync_RunsDriveSyncInBackground_ForDriveConnection()
    {
        // Arrange
        var connection = CreateConnection(serviceType: ServiceType.Drive);
        _connectionsMock
            .Setup(r => r.GetByIdTrackedAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        var signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _driveSyncMock
            .Setup(s => s.SyncConnectionAsync(connection, It.IsAny<CancellationToken>()))
            .Callback(() => signal.TrySetResult(true))
            .ReturnsAsync(new SyncResult(3, 3, 0, null));

        // Act
        var result = await _sut.TriggerManualSyncAsync(connection.Id, _userId);

        // Assert
        result.StatusCode.Should().Be(202);
        (await WaitAsync(signal)).Should().BeTrue("background task phải gọi Drive sync");
        _gmailSyncMock.Verify(
            s => s.SyncConnectionAsync(It.IsAny<Connection>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TriggerManualSyncAsync_SkipsUnsupportedServiceType_WithoutError()
    {
        // Arrange — Jira chưa nằm trong switch của manual sync
        var connection = CreateConnection(serviceType: ServiceType.Jira);
        _connectionsMock
            .Setup(r => r.GetByIdTrackedAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        var scopeCreated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _scopeFactoryMock
            .Setup(f => f.CreateScope())
            .Callback(() => scopeCreated.TrySetResult(true))
            .Returns(_scopeMock.Object);

        // Act
        var result = await _sut.TriggerManualSyncAsync(connection.Id, _userId);

        // Assert — vẫn 202, không sync service nào, connection không bị đánh Error
        result.StatusCode.Should().Be(202);
        (await WaitAsync(scopeCreated)).Should().BeTrue("background task phải tạo scope");
        connection.Status.Should().Be(ConnectionStatus.Active);
    }

    [Fact]
    public async Task TriggerManualSyncAsync_MarksConnectionError_WhenBackgroundSyncThrows()
    {
        // Arrange — provider trả lỗi (403/429/5xx) trong background task
        var connection = CreateConnection(serviceType: ServiceType.Gmail);
        _connectionsMock
            .Setup(r => r.GetByIdTrackedAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);
        _gmailSyncMock
            .Setup(s => s.SyncConnectionAsync(It.IsAny<Connection>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Google API 403"));

        var saved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _connectionsMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => saved.TrySetResult(true))
            .ReturnsAsync(1);

        // Act
        var result = await _sut.TriggerManualSyncAsync(connection.Id, _userId);

        // Assert — API vẫn trả 202 ngay, lỗi ghi vào connection để UI hiển thị
        result.StatusCode.Should().Be(202);
        (await WaitAsync(saved)).Should().BeTrue("background task phải lưu trạng thái lỗi");
        connection.Status.Should().Be(ConnectionStatus.Error);
        connection.LastError.Should().Contain("Google API 403");
    }
}
