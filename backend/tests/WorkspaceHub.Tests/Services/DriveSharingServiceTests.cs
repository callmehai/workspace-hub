using FluentAssertions;
using Moq;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs.Drive;
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

    private void SetupDriveItem(ConnectionStatus status = ConnectionStatus.Active)
    {
        SetupDriveConnection(status);
        _items.Setup(m => m.GetByIdAndUserAsync(_itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDriveFileItem());
    }

    private void SetupOwnerPermissionList()
    {
        _gateway.Setup(m => m.ListPermissionsAsync(It.IsAny<Connection>(), "drive-file-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DrivePermissionDto>
            {
                new()
                {
                    Id = "perm-owner",
                    Type = DrivePermissionTypes.User,
                    Role = DrivePermissionRoles.Owner,
                    IsOwner = true,
                    EmailAddress = "owner@example.com"
                }
            });
    }

    [Fact]
    public async Task UpdatePermission_Owner_ThrowsBusinessRule()
    {
        SetupDriveItem();
        SetupOwnerPermissionList();

        var act = () => _service.UpdatePermissionAsync(
            _userId, _itemId, "perm-owner", DrivePermissionRole.Writer);

        await act.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*owner*");

        _gateway.Verify(
            m => m.UpdatePermissionAsync(
                It.IsAny<Connection>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DrivePermissionRole>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RemovePermission_Owner_ThrowsBusinessRule()
    {
        SetupDriveItem();
        SetupOwnerPermissionList();

        var act = () => _service.RemovePermissionAsync(_userId, _itemId, "perm-owner");

        await act.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*owner*");

        _gateway.Verify(
            m => m.DeletePermissionAsync(
                It.IsAny<Connection>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SetLinkSharing_Enable_ReturnsPermissionFromGateway()
    {
        SetupDriveItem();

        var linkPerm = new DrivePermissionDto
        {
            Id = "link-1",
            Type = DrivePermissionTypes.Anyone,
            Role = DrivePermissionRoles.Reader,
            IsLink = true
        };

        _gateway.Setup(m => m.SetLinkSharingAsync(
                It.IsAny<Connection>(), "drive-file-1", true, DrivePermissionRole.Reader, It.IsAny<CancellationToken>()))
            .ReturnsAsync(linkPerm);

        var result = await _service.SetLinkSharingAsync(_userId, _itemId, true, DrivePermissionRole.Reader);

        result.Should().NotBeNull();
        result!.IsLink.Should().BeTrue();
    }

    [Fact]
    public async Task SetLinkSharing_Disable_ReturnsNull()
    {
        SetupDriveItem();

        _gateway.Setup(m => m.SetLinkSharingAsync(
                It.IsAny<Connection>(), "drive-file-1", false, It.IsAny<DrivePermissionRole>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DrivePermissionDto?)null);

        var result = await _service.SetLinkSharingAsync(_userId, _itemId, false);

        result.Should().BeNull();
    }

    /// <summary>
    /// Regression: controller đã Detect (không conflict) → skipConflictDetect tránh gọi lại ListPermissions/GetFile.
    /// </summary>
    [Fact]
    public async Task SetLinkSharing_Disable_SkipConflictDetect_DoesNotCallDetectApis()
    {
        // Có parents → nếu Detect chạy sẽ ListPermissions file + mẹ; skip phải không gọi.
        const string parentId = "parent-folder-skip";
        SetupDriveConnection();
        _items.Setup(m => m.GetByIdAndUserAsync(_itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDriveFileItem(
                $$"""{"mimeType":"application/pdf","parents":["{{parentId}}"]}"""));

        _gateway.Setup(m => m.SetLinkSharingAsync(
                It.IsAny<Connection>(), "drive-file-1", false, It.IsAny<DrivePermissionRole>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DrivePermissionDto?)null);

        var result = await _service.SetLinkSharingAsync(
            _userId,
            _itemId,
            enabled: false,
            confirmRestrictParent: false,
            skipConflictDetect: true);

        result.Should().BeNull();
        _gateway.Verify(
            m => m.ListPermissionsAsync(It.IsAny<Connection>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _gateway.Verify(
            m => m.GetFileAsync(It.IsAny<Connection>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _gateway.Verify(
            m => m.SetLinkSharingAsync(
                It.IsAny<Connection>(), "drive-file-1", false,
                It.IsAny<DrivePermissionRole>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SetLinkSharing_Disable_Case1WithoutConfirm_ThrowsConflict()
    {
        // Case 1: tắt link mà chưa confirm → 409 (ConflictException).
        const string parentId = "parent-folder-1";
        SetupDriveConnection();
        _items.Setup(m => m.GetByIdAndUserAsync(_itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDriveFileItem(
                $$"""{"mimeType":"application/pdf","parents":["{{parentId}}"]}"""));
        _items.Setup(m => m.GetByConnectionAndExternalIdAsync(
                _userId, _connId, parentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Item?)null);
        _gateway.Setup(m => m.GetFileAsync(It.IsAny<Connection>(), parentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DriveFile(parentId, null, "Dungtestfolder", DriveMimeTypes.Folder));

        SetupAnyoneLinkOnFileAndParent(parentId);

        var act = () => _service.SetLinkSharingAsync(
            _userId, _itemId, enabled: false, confirmRestrictParent: false);

        await act.Should().ThrowAsync<ConflictException>();
        _gateway.Verify(
            m => m.SetLinkSharingAsync(
                It.IsAny<Connection>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<DrivePermissionRole>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SetLinkSharing_Disable_Case1WithConfirm_RestrictsFileAndParent()
    {
        // Confirm popup → tắt link cả file lẫn folder mẹ.
        const string parentId = "parent-folder-1";
        SetupDriveConnection();
        _items.Setup(m => m.GetByIdAndUserAsync(_itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDriveFileItem(
                $$"""{"mimeType":"application/pdf","parents":["{{parentId}}"]}"""));
        _items.Setup(m => m.GetByConnectionAndExternalIdAsync(
                _userId, _connId, parentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Item
            {
                Id = Guid.NewGuid(),
                Title = "Dungtestfolder",
                ExternalId = parentId,
                ConnectionId = _connId,
                UserId = _userId,
                Type = ItemType.File,
                MetadataJson = """{"isFolder":true}"""
            });

        SetupAnyoneLinkOnFileAndParent(parentId);
        // Sau Detect: Ensure/list lại thấy đã tắt (giả lập Google đã apply).
        SetupLinkClearedAfterFirstList(parentId);

        _gateway.Setup(m => m.SetLinkSharingAsync(
                It.IsAny<Connection>(), parentId, false, It.IsAny<DrivePermissionRole>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DrivePermissionDto?)null);
        _gateway.Setup(m => m.SetLinkSharingAsync(
                It.IsAny<Connection>(), "drive-file-1", false, It.IsAny<DrivePermissionRole>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DrivePermissionDto?)null);

        var result = await _service.SetLinkSharingAsync(
            _userId, _itemId, enabled: false, confirmRestrictParent: true);

        result.Should().BeNull();
        _gateway.Verify(
            m => m.SetLinkSharingAsync(
                It.IsAny<Connection>(), parentId, false, It.IsAny<DrivePermissionRole>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _gateway.Verify(
            m => m.SetLinkSharingAsync(
                It.IsAny<Connection>(), "drive-file-1", false, It.IsAny<DrivePermissionRole>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SetLinkSharing_Disable_Case1WithConfirm_FileForbidden_StillSucceeds()
    {
        // Sau khi tắt mẹ, xoá anyone trên file bị 403 (kế thừa) → vẫn OK.
        const string parentId = "parent-folder-1";
        SetupDriveConnection();
        _items.Setup(m => m.GetByIdAndUserAsync(_itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDriveFileItem(
                $$"""{"mimeType":"application/pdf","parents":["{{parentId}}"]}"""));
        _items.Setup(m => m.GetByConnectionAndExternalIdAsync(
                _userId, _connId, parentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Item
            {
                Id = Guid.NewGuid(),
                Title = "Dungtestfolder",
                ExternalId = parentId,
                ConnectionId = _connId,
                UserId = _userId,
                Type = ItemType.File,
                MetadataJson = """{"isFolder":true}"""
            });

        SetupAnyoneLinkOnFileAndParent(parentId);
        SetupLinkClearedAfterFirstList(parentId);

        _gateway.Setup(m => m.SetLinkSharingAsync(
                It.IsAny<Connection>(), parentId, false, It.IsAny<DrivePermissionRole>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DrivePermissionDto?)null);
        _gateway.Setup(m => m.SetLinkSharingAsync(
                It.IsAny<Connection>(), "drive-file-1", false, It.IsAny<DrivePermissionRole>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Không đủ quyền gỡ chia sẻ."));

        var result = await _service.SetLinkSharingAsync(
            _userId, _itemId, enabled: false, confirmRestrictParent: true);

        result.Should().BeNull();
        _gateway.Verify(
            m => m.SetLinkSharingAsync(
                It.IsAny<Connection>(), parentId, false, It.IsAny<DrivePermissionRole>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SetLinkSharing_Disable_Case1WithConfirm_ParentForbidden_ThrowsBusinessRule()
    {
        // User chỉ là editor không đủ quyền đổi sharing folder mẹ → tắt link mẹ bị 403.
        // Phải trả BusinessRuleException (thông báo rõ) thay vì lỗi thô.
        const string parentId = "parent-folder-1";
        SetupDriveConnection();
        _items.Setup(m => m.GetByIdAndUserAsync(_itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDriveFileItem(
                $$"""{"mimeType":"application/pdf","parents":["{{parentId}}"]}"""));
        _items.Setup(m => m.GetByConnectionAndExternalIdAsync(
                _userId, _connId, parentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Item
            {
                Id = Guid.NewGuid(),
                Title = "Dungtestfolder",
                ExternalId = parentId,
                ConnectionId = _connId,
                UserId = _userId,
                Type = ItemType.File,
                MetadataJson = """{"isFolder":true}"""
            });

        SetupAnyoneLinkOnFileAndParent(parentId);

        // Tắt link folder mẹ bị Google từ chối quyền.
        _gateway.Setup(m => m.SetLinkSharingAsync(
                It.IsAny<Connection>(), parentId, false, It.IsAny<DrivePermissionRole>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Không đủ quyền gỡ chia sẻ."));

        var act = () => _service.SetLinkSharingAsync(
            _userId, _itemId, enabled: false, confirmRestrictParent: true);

        await act.Should().ThrowAsync<BusinessRuleException>();

        // KHÔNG được động tới link file khi chưa tắt được mẹ.
        _gateway.Verify(
            m => m.SetLinkSharingAsync(
                It.IsAny<Connection>(), "drive-file-1", false, It.IsAny<DrivePermissionRole>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>Mock cả file + folder mẹ đang anyone (điều kiện Case 1).</summary>
    private void SetupAnyoneLinkOnFileAndParent(string parentExternalId)
    {
        _gateway.Setup(m => m.ListPermissionsAsync(It.IsAny<Connection>(), "drive-file-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DrivePermissionDto>
            {
                new() { Id = "link-file", Type = DrivePermissionTypes.Anyone, Role = "reader", IsLink = true }
            });
        _gateway.Setup(m => m.ListPermissionsAsync(It.IsAny<Connection>(), parentExternalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DrivePermissionDto>
            {
                new() { Id = "link-parent", Type = DrivePermissionTypes.Anyone, Role = "reader", IsLink = true }
            });
    }

    /// <summary>
    /// Lần list đầu (Detect) còn anyone; các lần sau (Ensure) đã tắt — giả lập Google apply xong.
    /// Gọi SAU SetupAnyoneLinkOnFileAndParent để ghi đè mock list.
    /// </summary>
    private void SetupLinkClearedAfterFirstList(string parentExternalId)
    {
        var fileCalls = 0;
        var parentCalls = 0;
        var anyoneFile = new List<DrivePermissionDto>
        {
            new() { Id = "link-file", Type = DrivePermissionTypes.Anyone, Role = "reader", IsLink = true }
        };
        var anyoneParent = new List<DrivePermissionDto>
        {
            new() { Id = "link-parent", Type = DrivePermissionTypes.Anyone, Role = "reader", IsLink = true }
        };
        var empty = new List<DrivePermissionDto>();

        _gateway.Setup(m => m.ListPermissionsAsync(It.IsAny<Connection>(), "drive-file-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++fileCalls <= 1 ? anyoneFile : empty);
        _gateway.Setup(m => m.ListPermissionsAsync(It.IsAny<Connection>(), parentExternalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++parentCalls <= 1 ? anyoneParent : empty);
    }

    [Fact]
    public async Task CreateFolder_InactiveConnection_ThrowsBusinessRule()
    {
        SetupDriveConnection(ConnectionStatus.Disconnected);

        var act = () => _service.CreateFolderAsync(_userId, _connId, "New folder");

        await act.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*không active*");
    }

    [Fact]
    public async Task ListPermissions_InactiveConnection_ThrowsBusinessRule()
    {
        SetupDriveItem(ConnectionStatus.Error);

        var act = () => _service.ListPermissionsAsync(_userId, _itemId);

        await act.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*không active*");
    }

    [Fact]
    public async Task CreateFolder_OtherUsersConnection_Throws404()
    {
        _connections.Setup(m => m.GetByIdAsync(_connId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Connection
            {
                Id = _connId,
                UserId = Guid.NewGuid(),
                ServiceType = ServiceType.Drive,
                Status = ConnectionStatus.Active
            });

        var act = () => _service.CreateFolderAsync(_userId, _connId, "Folder");

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ── DetectLinkRestrictConflictAsync (Case 1 — giống Google Drive) ──

    [Fact]
    public async Task DetectLinkRestrict_NoParents_ReturnsNull()
    {
        // File ở gốc My Drive — không có thư mục mẹ → không conflict.
        SetupDriveItem();

        var result = await _service.DetectLinkRestrictConflictAsync(_userId, _itemId);

        result.Should().BeNull();
        _gateway.Verify(
            m => m.ListPermissionsAsync(It.IsAny<Connection>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DetectLinkRestrict_ParentPrivate_FilePublic_ReturnsNull_Case2()
    {
        // Case 2: folder mẹ hạn chế, file đang anyone — Drive không hỏi → null.
        const string parentId = "parent-folder-1";
        SetupDriveConnection();
        _items.Setup(m => m.GetByIdAndUserAsync(_itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDriveFileItem(
                $$"""{"mimeType":"application/pdf","parents":["{{parentId}}"]}"""));

        _gateway.Setup(m => m.ListPermissionsAsync(It.IsAny<Connection>(), "drive-file-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DrivePermissionDto>
            {
                new() { Id = "link-file", Type = DrivePermissionTypes.Anyone, Role = "reader", IsLink = true }
            });
        _gateway.Setup(m => m.ListPermissionsAsync(It.IsAny<Connection>(), parentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DrivePermissionDto>
            {
                new() { Id = "owner", Type = DrivePermissionTypes.User, Role = "owner", IsOwner = true }
            });

        var result = await _service.DetectLinkRestrictConflictAsync(_userId, _itemId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task DetectLinkRestrict_BothAnyone_ReturnsConflict_Case1()
    {
        // Case 1: file + folder mẹ đều anyone → conflict (popup Drive).
        const string parentId = "parent-folder-1";
        var parentItemId = Guid.NewGuid();
        SetupDriveConnection();
        _items.Setup(m => m.GetByIdAndUserAsync(_itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDriveFileItem(
                $$"""{"mimeType":"application/pdf","parents":["{{parentId}}"],"isFolder":false}"""));
        _items.Setup(m => m.GetByConnectionAndExternalIdAsync(
                _userId, _connId, parentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Item
            {
                Id = parentItemId,
                UserId = _userId,
                Type = ItemType.File,
                ConnectionId = _connId,
                ExternalId = parentId,
                Title = "Dungtestfolder",
                MetadataJson = """{"isFolder":true}"""
            });

        _gateway.Setup(m => m.ListPermissionsAsync(It.IsAny<Connection>(), "drive-file-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DrivePermissionDto>
            {
                new() { Id = "link-file", Type = DrivePermissionTypes.Anyone, Role = "reader", IsLink = true }
            });
        _gateway.Setup(m => m.ListPermissionsAsync(It.IsAny<Connection>(), parentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DrivePermissionDto>
            {
                new() { Id = "link-parent", Type = DrivePermissionTypes.Anyone, Role = "reader", IsLink = true }
            });

        var result = await _service.DetectLinkRestrictConflictAsync(_userId, _itemId);

        result.Should().NotBeNull();
        result!.Code.Should().Be(DriveLinkRestrictConflict.RestrictAffectsParentCode);
        result.ParentExternalId.Should().Be(parentId);
        result.ParentItemId.Should().Be(parentItemId);
        result.ParentTitle.Should().Be("Dungtestfolder");
        result.ItemFromAccess.Should().Be("anyone");
        result.ItemToAccess.Should().Be("restricted");
        result.ParentFromAccess.Should().Be("anyone");
        result.ParentToAccess.Should().Be("restricted");
    }
}
