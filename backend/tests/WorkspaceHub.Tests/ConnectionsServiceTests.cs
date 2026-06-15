using Moq;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs.Connections;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.OAuth.Core;
using WorkspaceHub.Application.Security;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Tests;

/// <summary>
/// Unit tests cho SCRUM-14: List / Disconnect / Refresh connection.
/// Mock all dependencies to isolate ConnectionsService logic.
/// </summary>
public class ConnectionsServiceTests
{
    private readonly Mock<IIntegrationRepository> _integrationsMock;
    private readonly Mock<IConnectionRepository> _connectionsMock;
    private readonly Mock<IItemRepository> _itemsMock;
    private readonly Mock<ITokenProtector> _tokenProtectorMock;
    private readonly Mock<IDistributedCache> _cacheMock;
    private readonly Mock<IConfiguration> _configMock;
    private readonly Mock<IOAuthTokenClient> _tokenClientMock;
    private readonly Mock<IGenericRepository<ScheduledEmail>> _scheduledEmailsMock;
    private readonly ConnectionsService _sut;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _otherUserId = Guid.NewGuid();

    public ConnectionsServiceTests()
    {
        _integrationsMock = new Mock<IIntegrationRepository>();
        _connectionsMock = new Mock<IConnectionRepository>();
        _itemsMock = new Mock<IItemRepository>();
        _tokenProtectorMock = new Mock<ITokenProtector>();
        _cacheMock = new Mock<IDistributedCache>();
        _configMock = new Mock<IConfiguration>();
        _tokenClientMock = new Mock<IOAuthTokenClient>();
        _scheduledEmailsMock = new Mock<IGenericRepository<ScheduledEmail>>();

        // No provider strategies needed for SCRUM-14 tests
        var strategies = Enumerable.Empty<IProviderStrategy>();

        _sut = new ConnectionsService(
            _integrationsMock.Object,
            _connectionsMock.Object,
            _itemsMock.Object,
            _tokenProtectorMock.Object,
            _cacheMock.Object,
            strategies,
            _configMock.Object,
            _tokenClientMock.Object,
            _scheduledEmailsMock.Object);
    }

    // ───────────── Helpers ─────────────

    private Connection CreateConnection(
        Guid? userId = null,
        Guid? id = null,
        ServiceType serviceType = ServiceType.Gmail,
        ConnectionStatus status = ConnectionStatus.Active,
        string accessToken = "encrypted-access-token-12345678",
        string refreshToken = "encrypted-refresh-token-1234",
        string providerAccountId = "user@gmail.com")
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            UserId = userId ?? _userId,
            IntegrationId = Guid.NewGuid(),
            Provider = ProviderType.Google,
            ServiceType = serviceType,
            ProviderAccountId = providerAccountId,
            AccessTokenEncrypted = accessToken,
            RefreshTokenEncrypted = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            Status = status,
            CreatedAt = DateTime.UtcNow
        };

    private Integration CreateIntegration(string key = "google") => new()
    {
        Id = Guid.NewGuid(),
        Key = key,
        DisplayName = "Google Workspace",
        IconUrl = "https://google.com/favicon.ico",
        Description = "Gmail · Calendar · Drive",
        Provider = "Google",
        AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth",
        TokenEndpoint = "https://oauth2.googleapis.com/token",
        SupportedServices = "[\"Gmail\",\"GCal\",\"Drive\"]",
        IsEnabled = true
    };

    // ═══════════════════════════════════════════════════════════
    // GET /api/connections — GetConnectionsAsync
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task GetConnectionsAsync_ReturnsEmptyArray_WhenUserHasNoConnections()
    {
        // Arrange
        _connectionsMock
            .Setup(r => r.GetByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Connection>().AsReadOnly());

        // Act
        var result = await _sut.GetConnectionsAsync(_userId);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetConnectionsAsync_ReturnsMaskedToken()
    {
        // Arrange
        var connection = CreateConnection(accessToken: "encrypted-access-token-ABCD");

        _connectionsMock
            .Setup(r => r.GetByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Connection> { connection }.AsReadOnly());

        // Act
        var result = await _sut.GetConnectionsAsync(_userId);

        // Assert
        Assert.Single(result);
        var dto = result[0];
        Assert.Equal("****ABCD", dto.MaskedToken);
        Assert.DoesNotContain("encrypted-access-token", dto.MaskedToken);
    }

    [Fact]
    public async Task GetConnectionsAsync_ReturnsCorrectServiceTypePerConnection()
    {
        // Arrange — user has Gmail and Drive connections
        var gmailConn = CreateConnection(serviceType: ServiceType.Gmail);
        var driveConn = CreateConnection(serviceType: ServiceType.Drive);

        _connectionsMock
            .Setup(r => r.GetByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Connection> { gmailConn, driveConn }.AsReadOnly());

        // Act
        var result = await _sut.GetConnectionsAsync(_userId);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(result, c => c.ServiceType == "Gmail");
        Assert.Contains(result, c => c.ServiceType == "Drive");
    }

    [Fact]
    public async Task GetConnectionsAsync_OnlyReturnsConnectionsForCurrentUser()
    {
        // Arrange — only return connections for _userId, not _otherUserId
        var myConnection = CreateConnection(userId: _userId);

        _connectionsMock
            .Setup(r => r.GetByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Connection> { myConnection }.AsReadOnly());

        // Act
        var result = await _sut.GetConnectionsAsync(_userId);

        // Assert
        Assert.Single(result);
        Assert.Equal(myConnection.Id, result[0].Id);

        // Verify the repo was called with the correct userId
        _connectionsMock.Verify(r => r.GetByUserIdAsync(_userId, It.IsAny<CancellationToken>()), Times.Once);
        _connectionsMock.Verify(r => r.GetByUserIdAsync(_otherUserId, It.IsAny<CancellationToken>()), Times.Never);
    }

    // ═══════════════════════════════════════════════════════════
    // DELETE /api/connections/{id} — DisconnectAsync
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task DisconnectAsync_DeletesConnection_OnSuccess()
    {
        // Arrange
        var connection = CreateConnection();

        _connectionsMock
            .Setup(r => r.GetByIdTrackedAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);
        _itemsMock
            .Setup(r => r.NullifyConnectionIdAsync(connection.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _scheduledEmailsMock
            .Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ScheduledEmail>().AsReadOnly());
        _connectionsMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        await _sut.DisconnectAsync(connection.Id, _userId);

        // Assert — connection is removed
        _connectionsMock.Verify(r => r.Remove(connection), Times.Once);
        _connectionsMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DisconnectAsync_NullifiesItemsConnectionId()
    {
        // Arrange
        var connection = CreateConnection();

        _connectionsMock
            .Setup(r => r.GetByIdTrackedAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);
        _itemsMock
            .Setup(r => r.NullifyConnectionIdAsync(connection.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _scheduledEmailsMock
            .Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ScheduledEmail>().AsReadOnly());
        _connectionsMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        await _sut.DisconnectAsync(connection.Id, _userId);

        // Assert — Items.ConnectionId nullified before connection removed
        _itemsMock.Verify(r => r.NullifyConnectionIdAsync(connection.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DisconnectAsync_RemovesScheduledEmails()
    {
        // Arrange
        var connection = CreateConnection();
        var scheduledEmail = new ScheduledEmail
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            ConnectionId = connection.Id,
            Subject = "Test",
            BodyHtml = "<p>Test</p>",
            SendAt = DateTime.UtcNow.AddHours(1),
            Status = ScheduledEmailStatus.Pending
        };

        _connectionsMock
            .Setup(r => r.GetByIdTrackedAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);
        _itemsMock
            .Setup(r => r.NullifyConnectionIdAsync(connection.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _scheduledEmailsMock
            .Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ScheduledEmail> { scheduledEmail }.AsReadOnly());
        _connectionsMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        await _sut.DisconnectAsync(connection.Id, _userId);

        // Assert
        _scheduledEmailsMock.Verify(r => r.Remove(scheduledEmail), Times.Once);
    }

    [Fact]
    public async Task DisconnectAsync_ThrowsNotFound_WhenConnectionDoesNotExist()
    {
        // Arrange
        var connectionId = Guid.NewGuid();
        _connectionsMock
            .Setup(r => r.GetByIdTrackedAsync(connectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Connection?)null);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(
            () => _sut.DisconnectAsync(connectionId, _userId));
    }

    [Fact]
    public async Task DisconnectAsync_ThrowsForbidden_WhenConnectionBelongsToAnotherUser()
    {
        // Arrange
        var connection = CreateConnection(userId: _otherUserId);
        _connectionsMock
            .Setup(r => r.GetByIdTrackedAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        // Act & Assert
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _sut.DisconnectAsync(connection.Id, _userId));
    }

    // ═══════════════════════════════════════════════════════════
    // POST /api/connections/{id}/refresh — RefreshConnectionAsync
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task RefreshConnectionAsync_ReturnsNewExpiresAt_OnSuccess()
    {
        // Arrange
        var integration = CreateIntegration();
        var connection = CreateConnection();
        connection.IntegrationId = integration.Id;

        _connectionsMock
            .Setup(r => r.GetByIdTrackedAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);
        _tokenProtectorMock
            .Setup(p => p.Unprotect(connection.RefreshTokenEncrypted))
            .Returns("real-refresh-token");
        _tokenProtectorMock
            .Setup(p => p.Protect(It.IsAny<string>()))
            .Returns("new-encrypted-token");
        _integrationsMock
            .Setup(r => r.GetByIdAsync(integration.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(integration);

        // Setup config
        _configMock.Setup(c => c[$"OAuth:google:ClientId"]).Returns("client-id");
        _configMock.Setup(c => c[$"OAuth:google:ClientSecret"]).Returns("client-secret");

        // Token endpoint returns valid response
        var tokenResponse = """{"access_token":"new-access-token","expires_in":3600}""";
        _tokenClientMock
            .Setup(c => c.PostFormAsync(integration.TokenEndpoint, It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenResponse);

        _connectionsMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _sut.RefreshConnectionAsync(connection.Id, _userId);

        // Assert
        Assert.Equal(connection.Id, result.ConnectionId);
        Assert.Equal("Active", result.Status);
        Assert.True(result.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task RefreshConnectionAsync_UpdatesTokenInDb()
    {
        // Arrange
        var integration = CreateIntegration();
        var connection = CreateConnection();
        connection.IntegrationId = integration.Id;

        _connectionsMock
            .Setup(r => r.GetByIdTrackedAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);
        _tokenProtectorMock
            .Setup(p => p.Unprotect(connection.RefreshTokenEncrypted))
            .Returns("real-refresh-token");
        _tokenProtectorMock
            .Setup(p => p.Protect("new-access-token"))
            .Returns("new-encrypted-access-token");
        _integrationsMock
            .Setup(r => r.GetByIdAsync(integration.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(integration);
        _configMock.Setup(c => c[$"OAuth:google:ClientId"]).Returns("client-id");
        _configMock.Setup(c => c[$"OAuth:google:ClientSecret"]).Returns("client-secret");

        var tokenResponse = """{"access_token":"new-access-token","expires_in":3600}""";
        _tokenClientMock
            .Setup(c => c.PostFormAsync(integration.TokenEndpoint, It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenResponse);
        _connectionsMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        await _sut.RefreshConnectionAsync(connection.Id, _userId);

        // Assert — token updated and saved
        _connectionsMock.Verify(r => r.Update(It.Is<Connection>(c =>
            c.AccessTokenEncrypted == "new-encrypted-access-token" &&
            c.Status == ConnectionStatus.Active)), Times.Once);
        _connectionsMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshConnectionAsync_ThrowsBusinessRule_WhenRefreshTokenEmpty()
    {
        // Arrange — connection has empty refresh token
        var connection = CreateConnection(refreshToken: "");

        _connectionsMock
            .Setup(r => r.GetByIdTrackedAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);
        _connectionsMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act & Assert — 422
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _sut.RefreshConnectionAsync(connection.Id, _userId));

        Assert.Contains("not available", ex.Message);
    }

    [Fact]
    public async Task RefreshConnectionAsync_SetsStatusError_WhenRefreshTokenInvalid()
    {
        // Arrange — empty refresh token triggers Error status
        var connection = CreateConnection(refreshToken: "");

        _connectionsMock
            .Setup(r => r.GetByIdTrackedAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);
        _connectionsMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        try
        {
            await _sut.RefreshConnectionAsync(connection.Id, _userId);
        }
        catch (BusinessRuleException)
        {
            // expected
        }

        // Assert — Status set to Error
        Assert.Equal(ConnectionStatus.Error, connection.Status);
        _connectionsMock.Verify(r => r.Update(It.Is<Connection>(c => c.Status == ConnectionStatus.Error)), Times.Once);
    }

    [Fact]
    public async Task RefreshConnectionAsync_SetsStatusError_WhenProviderRejectsRefreshToken()
    {
        // Arrange
        var integration = CreateIntegration();
        var connection = CreateConnection();
        connection.IntegrationId = integration.Id;

        _connectionsMock
            .Setup(r => r.GetByIdTrackedAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);
        _tokenProtectorMock
            .Setup(p => p.Unprotect(connection.RefreshTokenEncrypted))
            .Returns("real-refresh-token");
        _integrationsMock
            .Setup(r => r.GetByIdAsync(integration.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(integration);
        _configMock.Setup(c => c[$"OAuth:google:ClientId"]).Returns("client-id");
        _configMock.Setup(c => c[$"OAuth:google:ClientSecret"]).Returns("client-secret");

        // Provider returns error response (no access_token)
        var errorResponse = """{"error":"invalid_grant","error_description":"Token has been revoked."}""";
        _tokenClientMock
            .Setup(c => c.PostFormAsync(integration.TokenEndpoint, It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(errorResponse);
        _connectionsMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act & Assert — 422
        await Assert.ThrowsAsync<BusinessRuleException>(
            () => _sut.RefreshConnectionAsync(connection.Id, _userId));

        // Assert — Status set to Error
        Assert.Equal(ConnectionStatus.Error, connection.Status);
    }

    [Fact]
    public async Task RefreshConnectionAsync_ThrowsNotFound_WhenConnectionDoesNotExist()
    {
        // Arrange
        var connectionId = Guid.NewGuid();
        _connectionsMock
            .Setup(r => r.GetByIdTrackedAsync(connectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Connection?)null);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(
            () => _sut.RefreshConnectionAsync(connectionId, _userId));
    }

    [Fact]
    public async Task RefreshConnectionAsync_ThrowsForbidden_WhenConnectionBelongsToAnotherUser()
    {
        // Arrange
        var connection = CreateConnection(userId: _otherUserId);
        _connectionsMock
            .Setup(r => r.GetByIdTrackedAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        // Act & Assert
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _sut.RefreshConnectionAsync(connection.Id, _userId));
    }
}
