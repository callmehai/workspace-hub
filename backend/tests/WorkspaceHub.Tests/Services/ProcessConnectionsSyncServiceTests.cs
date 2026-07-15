using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs.Sync;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using WorkspaceHub.Infrastructure.Services;
using Xunit;

namespace WorkspaceHub.Tests.Services;

public class ProcessConnectionsSyncServiceTests
{
    private readonly Mock<IConnectionRepository> _connections = new();
    private readonly Mock<IConnectionSyncDispatcher> _dispatcher = new();
    private readonly Mock<IConnectionsService> _connectionsService = new();
    private readonly Mock<ILogger<ProcessConnectionsSyncService>> _logger = new();
    private readonly ProcessConnectionsSyncService _sut;

    public ProcessConnectionsSyncServiceTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Sync:DebounceSeconds"] = "30" })
            .Build();

        _sut = new ProcessConnectionsSyncService(
            _connections.Object, _dispatcher.Object, _connectionsService.Object,
            _logger.Object, config);
    }

    [Fact]
    public async Task ProcessConnectionsSyncAsync_NoConnections_ReturnsZeros()
    {
        _connections.Setup(r => r.GetActiveConnectionsToSyncAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Connection>());

        var result = await _sut.ProcessConnectionsSyncAsync();

        result.TotalConnections.Should().Be(0);
        result.SuccessCount.Should().Be(0);
    }

    [Fact]
    public async Task ProcessConnectionsSyncAsync_ValidConnection_SyncsSuccessfully()
    {
        var conn = ActiveConnection(lastSyncedMinutesAgo: 5);
        SetupTracked(conn);
        _dispatcher.Setup(d => d.SyncAsync(conn.Id, conn.UserId, It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new SyncResult(10, 2, 1, "cursor"));

        var result = await _sut.ProcessConnectionsSyncAsync();

        result.SuccessCount.Should().Be(1);
        _dispatcher.Verify(d => d.SyncAsync(conn.Id, conn.UserId, It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    public async Task ProcessConnectionsSyncAsync_Debounce_SkipsRecentSync()
    {
        var conn = ActiveConnection(lastSyncedSecondsAgo: 10);
        SetupTracked(conn);

        var result = await _sut.ProcessConnectionsSyncAsync();

        result.SkippedCount.Should().Be(1);
        _dispatcher.Verify(d => d.SyncAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task ProcessConnectionsSyncAsync_SyncThrows_Transient_KeepsActiveAndIncrementsErrorCount()
    {
        var conn = ActiveConnection(lastSyncedMinutesAgo: 5);
        SetupTracked(conn);
        _dispatcher.Setup(d => d.SyncAsync(conn.Id, conn.UserId, It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ThrowsAsync(new InvalidOperationException("API fail"));

        var result = await _sut.ProcessConnectionsSyncAsync();

        result.ErrorCount.Should().Be(1);
        conn.Status.Should().Be(ConnectionStatus.Active);
    }

    [Fact]
    public async Task ProcessConnectionsSyncAsync_SyncThrows_ProviderForbidden_MarksConnectionError()
    {
        var conn = ActiveConnection(lastSyncedMinutesAgo: 5);
        SetupTracked(conn);
        _dispatcher.Setup(d => d.SyncAsync(conn.Id, conn.UserId, It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ThrowsAsync(new ProviderException("Google API trả về lỗi 403: invalid_grant", HttpStatusCode.Forbidden));

        var result = await _sut.ProcessConnectionsSyncAsync();

        result.ErrorCount.Should().Be(1);
        conn.Status.Should().Be(ConnectionStatus.Error);
    }

    [Fact]
    public async Task ProcessConnectionsSyncAsync_SyncThrows_ProviderServerError_KeepsActive()
    {
        var conn = ActiveConnection(lastSyncedMinutesAgo: 5);
        SetupTracked(conn);
        _dispatcher.Setup(d => d.SyncAsync(conn.Id, conn.UserId, It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ThrowsAsync(new ProviderException("Google API trả về lỗi 503: unavailable", HttpStatusCode.ServiceUnavailable));

        var result = await _sut.ProcessConnectionsSyncAsync();

        result.ErrorCount.Should().Be(1);
        conn.Status.Should().Be(ConnectionStatus.Active);
    }

    [Fact]
    public async Task ProcessConnectionsSyncAsync_SyncThrows_Forbidden_MarksConnectionError()
    {
        var conn = ActiveConnection(lastSyncedMinutesAgo: 5);
        SetupTracked(conn);
        _dispatcher.Setup(d => d.SyncAsync(conn.Id, conn.UserId, It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ThrowsAsync(new ForbiddenException("Token revoked"));

        var result = await _sut.ProcessConnectionsSyncAsync();

        result.ErrorCount.Should().Be(1);
        conn.Status.Should().Be(ConnectionStatus.Error);
    }

    private static Connection ActiveConnection(int lastSyncedMinutesAgo = 5, int lastSyncedSecondsAgo = -1)
    {
        var lastSynced = lastSyncedSecondsAgo >= 0
            ? DateTime.UtcNow.AddSeconds(-lastSyncedSecondsAgo)
            : DateTime.UtcNow.AddMinutes(-lastSyncedMinutesAgo);

        return new Connection
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Status = ConnectionStatus.Active,
            ServiceType = ServiceType.Gmail,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            LastSyncedAt = lastSynced
        };
    }

    private void SetupTracked(Connection conn)
    {
        _connections.Setup(r => r.GetActiveConnectionsToSyncAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Connection> { conn });
        _connections.Setup(r => r.GetByIdTrackedAsync(conn.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(conn);
    }
}
