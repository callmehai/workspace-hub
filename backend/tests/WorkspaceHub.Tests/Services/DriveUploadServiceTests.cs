using FluentAssertions;
using Moq;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Mapping;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests.Services;

/// <summary>
/// Unit test DriveUploadService — mock gateway/repo, không gọi Google thật.
/// </summary>
public class DriveUploadServiceTests
{
    private readonly Mock<IDriveGateway> _gateway = new();
    private readonly Mock<IItemRepository> _items = new();
    private readonly Mock<IConnectionRepository> _connections = new();
    private readonly Mock<IDriveItemMapper> _mapper = new();
    private readonly DriveUploadService _service;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _connId = Guid.NewGuid();

    public DriveUploadServiceTests()
    {
        _service = new DriveUploadService(
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

    // ── UploadFileAsync ──────────────────────────────────────────────

    [Fact]
    public async Task UploadFile_ExceedsMaxSize_ThrowsBusinessRule()
    {
        SetupDriveConnection();
        var stream = new MemoryStream([1, 2, 3]);
        var overLimit = DriveUploadLimits.MaxFileBytes + 1;

        var act = () => _service.UploadFileAsync(
            _userId, _connId, "big.bin", "application/octet-stream", stream, overLimit);

        await act.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*100 MB*");
    }

    [Fact]
    public async Task UploadFile_EmptyFile_ThrowsBusinessRule()
    {
        SetupDriveConnection();
        var stream = new MemoryStream();

        var act = () => _service.UploadFileAsync(
            _userId, _connId, "empty.txt", "text/plain", stream, 0);

        await act.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*rỗng*");
    }

    [Fact]
    public async Task UploadFile_GmailConnection_ThrowsBusinessRule()
    {
        _connections.Setup(m => m.GetByIdAsync(_connId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Connection
            {
                Id = _connId,
                UserId = _userId,
                ServiceType = ServiceType.Gmail,
                Status = ConnectionStatus.Active
            });

        var stream = new MemoryStream([1, 2, 3]);

        var act = () => _service.UploadFileAsync(
            _userId, _connId, "doc.pdf", "application/pdf", stream, 3);

        await act.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*không phải Google Drive*");
    }

    [Fact]
    public async Task UploadFile_ParentNotFolder_ThrowsBusinessRule()
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

        var stream = new MemoryStream([1]);

        var act = () => _service.UploadFileAsync(
            _userId, _connId, "doc.pdf", "application/pdf", stream, 1, parentId);

        await act.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*folder Drive*");
    }

    [Fact]
    public async Task UploadFile_Valid_CallsGatewayAndInsertsItem()
    {
        SetupDriveConnection();

        var driveDto = new DriveFileDto
        {
            Id = "drive-upload-1",
            Name = "doc.pdf",
            MimeType = "application/pdf",
            Size = 3
        };

        _gateway.Setup(m => m.UploadFileAsync(
                It.IsAny<Connection>(),
                "doc.pdf",
                "application/pdf",
                null,
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(driveDto);

        var mappedItem = new Item
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            Type = ItemType.File,
            Title = "doc.pdf",
            ExternalId = "drive-upload-1",
            ConnectionId = _connId
        };

        _mapper.Setup(m => m.ToItem(driveDto, _userId, _connId))
            .Returns(mappedItem);

        var stream = new MemoryStream([1, 2, 3]);
        var result = await _service.UploadFileAsync(
            _userId, _connId, "doc.pdf", "application/pdf", stream, 3);

        result.Title.Should().Be("doc.pdf");
        _gateway.Verify(m => m.UploadFileAsync(
            It.IsAny<Connection>(), "doc.pdf", "application/pdf", null,
            It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
        _items.Verify(m => m.AddAsync(mappedItem, It.IsAny<CancellationToken>()), Times.Once);
        _items.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── UploadFolderAsync ────────────────────────────────────────────

    [Fact]
    public async Task UploadFolder_EmptyList_ThrowsBusinessRule()
    {
        SetupDriveConnection();

        var act = () => _service.UploadFolderAsync(_userId, _connId, []);

        await act.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*Không có file*");
    }

    [Fact]
    public async Task UploadFolder_TooManyFiles_ThrowsBusinessRule()
    {
        SetupDriveConnection();

        var entries = Enumerable.Range(0, DriveUploadLimits.MaxFolderFileCount + 1)
            .Select(i => new DriveFolderUploadEntry(
                $"f{i}.txt", $"f{i}.txt", "text/plain", new MemoryStream([1]), 1))
            .ToList();

        var act = () => _service.UploadFolderAsync(_userId, _connId, entries);

        await act.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage($"*{DriveUploadLimits.MaxFolderFileCount}*");
    }

    [Fact]
    public async Task UploadFolder_TotalSizeExceeded_ThrowsBusinessRule()
    {
        SetupDriveConnection();

        // 6 file × 90 MB = 540 MB > 500 MB limit (mỗi file vẫn < 100 MB)
        var fileSize = 90L * 1024 * 1024;
        var entries = Enumerable.Range(0, 6)
            .Select(i => new DriveFolderUploadEntry(
                $"f{i}.bin", $"f{i}.bin", "application/octet-stream",
                new MemoryStream([1]), fileSize))
            .ToList();

        var act = () => _service.UploadFolderAsync(_userId, _connId, entries);

        await act.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*500 MB*");
    }

    [Fact]
    public async Task UploadFolder_WithSubfolder_CreatesFolderThenUploads()
    {
        SetupDriveConnection();

        var folderDto = new DriveFileDto
        {
            Id = "folder-docs",
            Name = "docs",
            MimeType = DriveMimeTypes.Folder
        };

        var fileDto = new DriveFileDto
        {
            Id = "file-readme",
            Name = "readme.txt",
            MimeType = "text/plain",
            Size = 5
        };

        _gateway.Setup(m => m.CreateFolderAsync(
                It.IsAny<Connection>(), "docs", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(folderDto);

        _gateway.Setup(m => m.UploadFileAsync(
                It.IsAny<Connection>(),
                "readme.txt",
                "text/plain",
                "folder-docs",
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileDto);

        _mapper.Setup(m => m.ToItem(folderDto, _userId, _connId))
            .Returns(new Item { Id = Guid.NewGuid(), Title = "docs", Type = ItemType.File });

        _mapper.Setup(m => m.ToItem(fileDto, _userId, _connId))
            .Returns(new Item { Id = Guid.NewGuid(), Title = "readme.txt", Type = ItemType.File });

        var entries = new List<DriveFolderUploadEntry>
        {
            new("docs/readme.txt", "readme.txt", "text/plain", new MemoryStream([1, 2, 3, 4, 5]), 5)
        };

        var result = await _service.UploadFolderAsync(_userId, _connId, entries);

        result.FilesUploaded.Should().Be(1);
        result.FoldersCreated.Should().Be(1);
        result.Items.Should().HaveCount(2); // 1 folder + 1 file
        _gateway.Verify(m => m.CreateFolderAsync(
            It.IsAny<Connection>(), "docs", null, It.IsAny<CancellationToken>()), Times.Once);
        _gateway.Verify(m => m.UploadFileAsync(
            It.IsAny<Connection>(), "readme.txt", "text/plain", "folder-docs",
            It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
        _items.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
