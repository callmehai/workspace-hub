using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using WorkspaceHub.Application.Abstractions;
using Xunit;

namespace WorkspaceHub.Tests.Services;

public class ConnectionHealthCheckerTests
{
    private readonly Mock<IConnectionRepository> _connectionRepoMock;
    private readonly Mock<IConnectionsService> _connectionsServiceMock;
    private readonly Mock<IGmailSyncService> _syncServiceMock;
    private readonly Mock<ILogger<ConnectionHealthChecker>> _loggerMock;
    private readonly Mock<IConfiguration> _configMock;
    private readonly ConnectionHealthChecker _checker;

    public ConnectionHealthCheckerTests()
    {
        _connectionRepoMock = new Mock<IConnectionRepository>();
        _connectionsServiceMock = new Mock<IConnectionsService>();
        _syncServiceMock = new Mock<IGmailSyncService>();
        _loggerMock = new Mock<ILogger<ConnectionHealthChecker>>();
        
        _configMock = new Mock<IConfiguration>();
        var configSectionMock = new Mock<IConfigurationSection>();
        configSectionMock.Setup(c => c.Value).Returns("30");
        _configMock.Setup(c => c.GetSection("Sync:DebounceSeconds")).Returns(configSectionMock.Object);

        _checker = new ConnectionHealthChecker(
            _connectionRepoMock.Object,
            _connectionsServiceMock.Object,
            _syncServiceMock.Object,
            _loggerMock.Object,
            _configMock.Object);
    }

    [Fact]
    public async Task EnsureAllSyncedAsync_NoConnections_ReturnsSilently()
    {
        var userId = Guid.NewGuid();
        _connectionRepoMock
            .Setup(r => r.GetActiveConnectionsForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Connection>());

        await _checker.EnsureAllSyncedAsync(userId);

        _connectionRepoMock.Verify(r => r.GetByIdTrackedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _syncServiceMock.Verify(s => s.SyncConnectionAsync(It.IsAny<Connection>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnsureAllSyncedAsync_TokenValid_SyncsSuccessfully()
    {
        var userId = Guid.NewGuid();
        var conn = new Connection
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Status = ConnectionStatus.Active,
            ServiceType = ServiceType.Gmail,
            ExpiresAt = DateTime.UtcNow.AddMinutes(60), // Valid
            LastSyncedAt = DateTime.UtcNow.AddMinutes(-5) // Past debounce
        };

        _connectionRepoMock
            .Setup(r => r.GetActiveConnectionsForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Connection> { conn });
        _connectionRepoMock
            .Setup(r => r.GetByIdTrackedAsync(conn.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(conn);
        _syncServiceMock
            .Setup(s => s.SyncConnectionAsync(conn, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncResult(10, 5, 0, "cursor"));

        await _checker.EnsureAllSyncedAsync(userId);

        _connectionsServiceMock.Verify(s => s.RefreshConnectionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _syncServiceMock.Verify(s => s.SyncConnectionAsync(conn, 50, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnsureAllSyncedAsync_TokenExpired_AutoRefreshesThenSyncs()
    {
        var userId = Guid.NewGuid();
        var conn = new Connection
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Status = ConnectionStatus.Active,
            ServiceType = ServiceType.Gmail,
            ExpiresAt = DateTime.UtcNow.AddMinutes(2), // Needs refresh
            LastSyncedAt = DateTime.UtcNow.AddMinutes(-5) // Past debounce
        };

        var refreshedConn = new Connection
        {
            Id = conn.Id,
            UserId = userId,
            Status = ConnectionStatus.Active,
            ServiceType = ServiceType.Gmail,
            ExpiresAt = DateTime.UtcNow.AddMinutes(60), // Refreshed
            LastSyncedAt = DateTime.UtcNow.AddMinutes(-5)
        };

        _connectionRepoMock
            .Setup(r => r.GetActiveConnectionsForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Connection> { conn });
        _connectionRepoMock
            .SetupSequence(r => r.GetByIdTrackedAsync(conn.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(conn)         // First call before refresh
            .ReturnsAsync(refreshedConn); // Second call after refresh

        _syncServiceMock
            .Setup(s => s.SyncConnectionAsync(refreshedConn, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncResult(10, 5, 0, "cursor"));

        await _checker.EnsureAllSyncedAsync(userId);

        _connectionsServiceMock.Verify(s => s.RefreshConnectionAsync(conn.Id, userId, It.IsAny<CancellationToken>()), Times.Once);
        _syncServiceMock.Verify(s => s.SyncConnectionAsync(refreshedConn, 50, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnsureAllSyncedAsync_RefreshFails_SkipsSyncButDoesNotBlockOthers()
    {
        var userId = Guid.NewGuid();
        var conn1 = new Connection
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Status = ConnectionStatus.Active,
            ServiceType = ServiceType.Gmail,
            ExpiresAt = DateTime.UtcNow.AddMinutes(2) // Needs refresh
        };
        var conn2 = new Connection
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Status = ConnectionStatus.Active,
            ServiceType = ServiceType.Gmail,
            ExpiresAt = DateTime.UtcNow.AddMinutes(60) // Valid
        };

        _connectionRepoMock
            .Setup(r => r.GetActiveConnectionsForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Connection> { conn1, conn2 });

        _connectionRepoMock
            .Setup(r => r.GetByIdTrackedAsync(conn1.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(conn1);
        _connectionRepoMock
            .Setup(r => r.GetByIdTrackedAsync(conn2.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(conn2);

        // Refresh fails for conn1
        _connectionsServiceMock
            .Setup(s => s.RefreshConnectionAsync(conn1.Id, userId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Revoked"));

        await _checker.EnsureAllSyncedAsync(userId);

        // Conn1 sync is skipped
        _syncServiceMock.Verify(s => s.SyncConnectionAsync(conn1, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        
        // Assert conn1 is marked as needing re-auth
        Assert.Equal(ConnectionStatus.Error, conn1.Status);
        Assert.NotNull(conn1.LastError);
        Assert.Contains("Token expired and auto-refresh failed", conn1.LastError);
        _connectionRepoMock.Verify(r => r.Update(conn1), Times.Once);
        _connectionRepoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        // Conn2 sync still happens
        _syncServiceMock.Verify(s => s.SyncConnectionAsync(conn2, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnsureAllSyncedAsync_Debounce_SkipsIfSyncedRecently()
    {
        var userId = Guid.NewGuid();
        var conn = new Connection
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Status = ConnectionStatus.Active,
            ServiceType = ServiceType.Gmail,
            ExpiresAt = DateTime.UtcNow.AddMinutes(60),
            LastSyncedAt = DateTime.UtcNow.AddSeconds(-10) // Synced 10 seconds ago, debounce is 30
        };

        _connectionRepoMock
            .Setup(r => r.GetActiveConnectionsForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Connection> { conn });
        _connectionRepoMock
            .Setup(r => r.GetByIdTrackedAsync(conn.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(conn);

        await _checker.EnsureAllSyncedAsync(userId);

        _syncServiceMock.Verify(s => s.SyncConnectionAsync(It.IsAny<Connection>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
