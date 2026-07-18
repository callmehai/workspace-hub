using System.IO;
using System.Net.Http;
using FluentAssertions;
using Moq;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests.Services;

/// <summary>
/// Unit test cho <see cref="DriveContentService"/> — luồng READ proxy media (download/thumbnail).
/// Tập trung vào resolve item→connection (bảo mật: đúng user + đúng service + active) và folder guard,
/// ủy thác Google cho <see cref="IDriveGateway"/> (mock).
/// </summary>
public class DriveContentServiceTests
{
    private readonly Mock<IDriveGateway> _gateway = new();
    private readonly Mock<IItemRepository> _items = new();
    private readonly Mock<IConnectionRepository> _connections = new();
    private readonly Mock<IFolderRepository> _folders = new();
    private readonly DriveContentService _service;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _connId = Guid.NewGuid();
    private readonly Guid _itemId = Guid.NewGuid();

    public DriveContentServiceTests()
    {
        _service = new DriveContentService(_gateway.Object, _items.Object, _connections.Object, _folders.Object);
    }

    // ── helpers ────────────────────────────────────────────────────────────
    private Item DriveFile(string metadataJson = """{"mimeType":"image/png"}""") => new()
    {
        Id = _itemId,
        UserId = _userId,
        Type = ItemType.File,
        ConnectionId = _connId,
        ExternalId = "drive-file-1",
        Title = "photo.png",
        MetadataJson = metadataJson,
    };

    private void SetupItem(Item item) =>
        _items.Setup(m => m.GetByIdAndUserAsync(_itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

    private void SetupConnection(
        ServiceType service = ServiceType.Drive,
        ConnectionStatus status = ConnectionStatus.Active,
        Guid? ownerOverride = null) =>
        _connections.Setup(m => m.GetByIdAsync(_connId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Connection
            {
                Id = _connId,
                UserId = ownerOverride ?? _userId,
                ServiceType = service,
                Status = status,
                ProviderAccountId = "drive-1",
            });

    private static DriveMediaResult SentinelMedia() =>
        new(new HttpResponseMessage(), Stream.Null, "image/png", null);

    // ── DownloadAsync — resolve guards ──────────────────────────────────────
    [Fact]
    public async Task DownloadAsync_ItemNotFound_ThrowsNotFound()
    {
        _items.Setup(m => m.GetByIdAndUserAsync(_itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Item?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _service.DownloadAsync(_userId, _itemId));
    }

    [Fact]
    public async Task DownloadAsync_ItemNotFileType_ThrowsBusinessRule()
    {
        var email = DriveFile();
        email.Type = ItemType.Email;
        SetupItem(email);

        await Assert.ThrowsAsync<BusinessRuleException>(() => _service.DownloadAsync(_userId, _itemId));
    }

    [Fact]
    public async Task DownloadAsync_ItemMissingExternalId_ThrowsBusinessRule()
    {
        var item = DriveFile();
        item.ExternalId = null;
        SetupItem(item);

        await Assert.ThrowsAsync<BusinessRuleException>(() => _service.DownloadAsync(_userId, _itemId));
    }

    [Fact]
    public async Task DownloadAsync_ConnectionBelongsToAnotherUser_ThrowsNotFound()
    {
        SetupItem(DriveFile());
        SetupConnection(ownerOverride: Guid.NewGuid()); // connection của user khác → coi như không thấy

        await Assert.ThrowsAsync<NotFoundException>(() => _service.DownloadAsync(_userId, _itemId));
    }

    [Fact]
    public async Task DownloadAsync_ConnectionNotDrive_ThrowsBusinessRule()
    {
        SetupItem(DriveFile());
        SetupConnection(service: ServiceType.Gmail);

        await Assert.ThrowsAsync<BusinessRuleException>(() => _service.DownloadAsync(_userId, _itemId));
    }

    [Fact]
    public async Task DownloadAsync_ConnectionNotActive_ThrowsBusinessRule()
    {
        SetupItem(DriveFile());
        SetupConnection(status: ConnectionStatus.Disconnected);

        await Assert.ThrowsAsync<BusinessRuleException>(() => _service.DownloadAsync(_userId, _itemId));
    }

    // ── DownloadAsync — folder guard + happy path ───────────────────────────
    [Fact]
    public async Task DownloadAsync_FolderMime_ThrowsBusinessRule_WithoutCallingGateway()
    {
        SetupItem(DriveFile("""{"mimeType":"application/vnd.google-apps.folder"}"""));
        SetupConnection();

        await Assert.ThrowsAsync<BusinessRuleException>(() => _service.DownloadAsync(_userId, _itemId));

        _gateway.Verify(g => g.DownloadFileAsync(
            It.IsAny<Connection>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DownloadAsync_ValidFile_DelegatesToGatewayWithMimeAndTitle()
    {
        SetupItem(DriveFile());
        SetupConnection();
        var expected = SentinelMedia();
        _gateway.Setup(g => g.DownloadFileAsync(
                It.IsAny<Connection>(), "drive-file-1", "image/png", "photo.png", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _service.DownloadAsync(_userId, _itemId);

        result.Should().BeSameAs(expected);
        _gateway.VerifyAll();
    }

    // ── GetThumbnailAsync ───────────────────────────────────────────────────
    [Fact]
    public async Task GetThumbnailAsync_Folder_ReturnsNull_WithoutCallingGateway()
    {
        SetupItem(DriveFile("""{"mimeType":"application/vnd.google-apps.folder"}"""));
        SetupConnection();

        var result = await _service.GetThumbnailAsync(_userId, _itemId);

        result.Should().BeNull();
        _gateway.Verify(g => g.GetThumbnailAsync(
            It.IsAny<Connection>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetThumbnailAsync_ValidFile_DelegatesToGateway()
    {
        SetupItem(DriveFile());
        SetupConnection();
        var expected = SentinelMedia();
        _gateway.Setup(g => g.GetThumbnailAsync(
                It.IsAny<Connection>(), "drive-file-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _service.GetThumbnailAsync(_userId, _itemId);

        result.Should().BeSameAs(expected);
        _gateway.VerifyAll();
    }
}
