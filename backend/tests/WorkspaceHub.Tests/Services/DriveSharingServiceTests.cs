using FluentAssertions;
using Moq;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Mapping;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests.Services;

public class DriveSharingServiceTests
{
    private readonly Mock<IDriveGateway> _gateway = new();
    private readonly Mock<IItemRepository> _items = new();
    private readonly Mock<IConnectionRepository> _connections = new();
    private readonly Mock<IDriveItemMapper> _mapper = new();
    private readonly DriveSharingService _service;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _connId = Guid.NewGuid();
    private readonly Guid _itemId = Guid.NewGuid();

    public DriveSharingServiceTests()
    {
        _service = new DriveSharingService(
            _gateway.Object,
            _items.Object,
            _connections.Object,
            _mapper.Object);
    }

    private void SetupDriveConnection(ConnectionStatus status = ConnectionStatus.Active) =>
        _connections.Setup(m => m.GetByIdAsync(_connId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Connection
            {
                Id = _connId,
                UserId = _userId,
                ServiceType = ServiceType.Drive,
                Status = status,
                ProviderAccountId = "drive-1"
            });

    private Item CreateDriveFileItem(string metadataJson = """{"mimeType":"application/vnd.google-apps.document"}""") =>
        new()
        {
            Id = _itemId,
            UserId = _userId,
            Type = ItemType.File,
            ConnectionId = _connId,
            ExternalId = "drive-file-1",
            MetadataJson = metadataJson
        };

    [Fact]
    public async Task ListPermissions_GmailConnection_ThrowsBusinessRule()
    {
        _items.Setup(m => m.GetByIdAndUserAsync(_itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDriveFileItem());

        _connections.Setup(m => m.GetByIdAsync(_connId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Connection
            {
                Id = _connId,
                UserId = _userId,
                ServiceType = ServiceType.Gmail,
                Status = ConnectionStatus.Active
            });

        var act = () => _service.ListPermissionsAsync(_userId, _itemId);

        await act.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*không phải Google Drive*");
    }

    [Fact]
    public async Task ListPermissions_OtherUserItem_Throws404()
    {
        _items.Setup(m => m.GetByIdAndUserAsync(_itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Item?)null);

        var act = () => _service.ListPermissionsAsync(_userId, _itemId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task CreateFolder_ParentNotFolder_ThrowsBusinessRule()
    {
        SetupDriveConnection();

        var parentId = Guid.NewGuid();
        _items.Setup(m => m.GetByIdAndUserAsync(parentId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Item
            {
                Id = parentId,
                UserId = _userId,
                Type = ItemType.File,
                ConnectionId = _connId,
                ExternalId = "parent-file",
                MetadataJson = """{"mimeType":"application/pdf"}"""
            });

        var act = () => _service.CreateFolderAsync(_userId, _connId, "Sub", parentId);

        await act.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*folder Drive*");
    }

    [Fact]
    public async Task CreateFolder_Valid_CallsGatewayAndInsertsItem()
    {
        SetupDriveConnection();

        var driveDto = new DriveFileDto
        {
            Id = "new-folder-id",
            Name = "Hợp đồng",
            MimeType = DriveMimeTypes.Folder
        };

        _gateway.Setup(m => m.CreateFolderAsync(
                It.IsAny<Connection>(), "Hợp đồng", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(driveDto);

        _mapper.Setup(m => m.ToItem(driveDto, _userId, _connId))
            .Returns(new Item { Id = Guid.NewGuid(), Type = ItemType.File, Title = "Hợp đồng" });

        var result = await _service.CreateFolderAsync(_userId, _connId, "Hợp đồng");

        result.Title.Should().Be("Hợp đồng");
        _items.Verify(m => m.AddAsync(It.IsAny<Item>(), It.IsAny<CancellationToken>()), Times.Once);
        _items.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddPermission_DuplicateEmail_ThrowsConflict()
    {
        SetupDriveConnection();
        _items.Setup(m => m.GetByIdAndUserAsync(_itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDriveFileItem());

        _gateway.Setup(m => m.ListPermissionsAsync(It.IsAny<Connection>(), "drive-file-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DrivePermissionDto>
            {
                new()
                {
                    Id = "perm-1",
                    Type = DrivePermissionTypes.User,
                    Role = DrivePermissionRoles.Reader,
                    EmailAddress = "a@example.com"
                }
            });

        var act = () => _service.AddPermissionAsync(_userId, _itemId, "a@example.com", DrivePermissionRole.Reader);

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*đã được chia sẻ*");
    }
}
