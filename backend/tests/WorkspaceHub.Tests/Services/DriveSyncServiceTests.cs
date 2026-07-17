using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Mapping;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using Xunit;

namespace WorkspaceHub.Tests.Services;

public class DriveSyncServiceTests
{
    private readonly Mock<IGoogleDriveGateway> _gatewayMock;
    private readonly Mock<IDriveItemMapper> _mapperMock;
    private readonly Mock<IItemRepository> _itemsMock;
    private readonly Mock<IConnectionRepository> _connectionsMock;
    private readonly DriveSyncService _sut;

    public DriveSyncServiceTests()
    {
        _gatewayMock = new Mock<IGoogleDriveGateway>();
        _mapperMock = new Mock<IDriveItemMapper>();
        _itemsMock = new Mock<IItemRepository>();
        _connectionsMock = new Mock<IConnectionRepository>();

        _sut = new DriveSyncService(
            _gatewayMock.Object,
            _mapperMock.Object,
            _itemsMock.Object,
            _connectionsMock.Object);
    }

    [Fact]
    public async Task SyncConnectionAsync_OptimizedIsTopLevelExtraction_ShouldUpdateCorrectly()
    {
        // Arrange
        var connection = new Connection { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Status = WorkspaceHub.Domain.Enums.ConnectionStatus.Active };
        var parentId = "parent-folder-123";
        var childId = "child-file-456";

        var existingItems = new Dictionary<string, Item>
        {
            { 
                childId,
                new Item 
                { 
                    Id = Guid.NewGuid(), 
                    ExternalId = childId, 
                    MetadataJson = "{\"isTopLevel\":true,\"parents\":[\"" + parentId + "\"]}" 
                }
            }
        };

        var driveFilesFromApi = new List<DriveFileDto>
        {
            new DriveFileDto { Id = parentId, Name = "Folder", MimeType = "application/vnd.google-apps.folder" }
        };

        _itemsMock.Setup(r => r.GetTrackedByConnectionIdAsync(connection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingItems);

        _gatewayMock.Setup(g => g.SyncFilesAsync(connection, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DriveSyncResult(false, driveFilesFromApi, "next-token"));

        _mapperMock.Setup(m => m.ToItem(It.IsAny<DriveFileDto>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<bool?>()))
            .Returns((DriveFileDto dto, Guid userId, Guid connId, bool? isTopLevel) => new Item
            {
                Id = Guid.NewGuid(),
                ExternalId = dto.Id,
                MetadataJson = "{\"isFolder\":true}"
            });

        // Act
        await _sut.SyncConnectionAsync(connection, CancellationToken.None);

        // Assert
        // After syncing, the child's MetadataJson should be updated to isTopLevel: false
        existingItems[childId].MetadataJson.Should().Contain("\"isTopLevel\":false");
    }
}
