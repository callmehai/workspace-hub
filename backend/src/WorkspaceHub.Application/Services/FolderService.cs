using FluentValidation;
using Microsoft.Extensions.Logging;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Services;

/// <summary>
/// Business logic cho Folder CRUD và Sharing.
/// Chứa toàn bộ logic: ownership check, computed fields (itemCount, isOwner, permission, ownerName),
/// invite share, accept/decline, revoke.
/// Throw custom exception → middleware map sang status code (xem CONVENTIONS.md).
/// </summary>
public class FolderService : IFolderService
{
    private readonly IFolderRepository _folderRepo;
    private readonly IItemRepository _itemRepo;
    private readonly IFriendshipRepository _friendships;
    private readonly INotificationService _notifications;
    private readonly IValidator<InviteFolderShareRequest> _inviteValidator;
    private readonly IValidator<UpdateFolderShareRequest> _updateShareValidator;
    private readonly ILogger<FolderService> _logger;

    public FolderService(
        IFolderRepository folderRepo,
        IItemRepository itemRepo,
        IFriendshipRepository friendships,
        INotificationService notifications,
        IValidator<InviteFolderShareRequest> inviteValidator,
        IValidator<UpdateFolderShareRequest> updateShareValidator,
        ILogger<FolderService> logger)
    {
        _folderRepo = folderRepo;
        _itemRepo = itemRepo;
        _friendships = friendships;
        _notifications = notifications;
        _inviteValidator = inviteValidator;
        _updateShareValidator = updateShareValidator;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<FolderResponse>> GetFoldersAsync(
        Guid userId, bool includeShared, CancellationToken ct = default)
    {
        // 1. Folders owned by user (luôn trả)
        var ownedFolders = await _folderRepo.GetUserFoldersAsync(userId, ct);

        var result = new List<FolderResponse>();

        // Map owned folders → response DTO
        foreach (var folder in ownedFolders)
        {
            result.Add(MapToDto(folder, userId));
        }

        // 2. Folders shared with user (tuỳ chọn)
        if (includeShared)
        {
            var sharedFolders = await _folderRepo.GetSharedFoldersAsync(userId, ct);

            foreach (var folder in sharedFolders)
            {
                // Tránh duplicate nếu user vừa own vừa được share (edge case)
                if (result.Any(r => r.Id == folder.Id))
                    continue;

                result.Add(MapToDto(folder, userId));
            }
        }

        return result.AsReadOnly();
    }

    /// <inheritdoc/>
    public async Task<FolderResponse> CreateAsync(
        Guid userId, CreateFolderRequest request, CancellationToken ct = default)
    {
        // Auto-assign SortOrder = max + 1 cho drag-drop ordering
        var maxSort = await _folderRepo.GetMaxSortOrderAsync(userId, ct);

        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            OwnerId = userId,
            Name = request.Name,
            Color = request.Color,
            Icon = request.Icon,
            SortOrder = maxSort + 1,
            IsArchived = false
        };

        await _folderRepo.AddAsync(folder, ct);
        await _folderRepo.SaveChangesAsync(ct);

        // Reload with Owner navigation for response mapping
        var created = await _folderRepo.GetByIdWithOwnerAsync(folder.Id, ct)
            ?? throw new InvalidOperationException($"Folder {folder.Id} vừa tạo nhưng không reload được.");
        return MapToDto(created, userId);
    }

    /// <inheritdoc/>
    public async Task<FolderResponse> UpdateAsync(
        Guid userId, Guid folderId, UpdateFolderRequest request, CancellationToken ct = default)
    {
        var folder = await _folderRepo.GetByIdWithOwnerAsync(folderId, ct)
            ?? throw new NotFoundException(nameof(Folder), folderId);

        // Chỉ Owner mới được sửa (API.md: "Bearer (Owner)")
        if (folder.OwnerId != userId)
            throw new ForbiddenException("Only the folder owner can update this folder.");

        // Apply changes
        folder.Name = request.Name;
        folder.Color = request.Color;
        folder.Icon = request.Icon;
        folder.SortOrder = request.SortOrder;

        _folderRepo.Update(folder);
        await _folderRepo.SaveChangesAsync(ct);

        return MapToDto(folder, userId);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(
        Guid userId, Guid folderId, CancellationToken ct = default)
    {
        var folder = await _folderRepo.GetByIdWithOwnerAsync(folderId, ct)
            ?? throw new NotFoundException(nameof(Folder), folderId);

        // Chỉ Owner mới được xoá (API.md: "Bearer (Owner)")
        if (folder.OwnerId != userId)
            throw new ForbiddenException("Only the folder owner can delete this folder.");

        // Hard delete — DB cascade sẽ xoá ItemFolders + FolderShares.
        // Items được giữ lại (ItemFolder cascade chỉ xoá junction row, không xoá Item).
        // Xem CLAUDE.md: "Soft delete: KHÔNG dùng. Xoá thật khi Delete."
        _folderRepo.Remove(folder);
        await _folderRepo.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<ItemFolderResponse> AddItemToFolderAsync(
        Guid userId, Guid folderId, AddItemToFolderRequest request, CancellationToken ct = default)
    {
        var isOwner = await _folderRepo.ExistsByOwnerAsync(folderId, userId, ct);
        if (!isOwner)
            throw new ForbiddenException("Only the folder owner can add items to this folder.");

        var item = await _itemRepo.GetByIdAndUserAsync(request.ItemId, userId, ct);
        if (item == null)
            throw new ForbiddenException("The item does not belong to the current user or does not exist.");

        var exists = await _folderRepo.ItemFolderExistsAsync(request.ItemId, folderId, ct);
        if (exists)
            throw new ConflictException("Item is already in this folder.");

        var maxPos = await _folderRepo.GetMaxItemPositionAsync(folderId, ct);

        var itemFolder = new ItemFolder
        {
            ItemId = request.ItemId,
            FolderId = folderId,
            Position = maxPos + 1,
            AddedAt = DateTime.UtcNow
        };

        await _folderRepo.AddItemFolderAsync(itemFolder, ct);
        await _folderRepo.SaveChangesAsync(ct);

        return new ItemFolderResponse(
            ItemId: itemFolder.ItemId,
            FolderId: itemFolder.FolderId,
            Position: itemFolder.Position,
            AddedAt: itemFolder.AddedAt
        );
    }

    /// <inheritdoc/>
    public async Task AddItemsToFolderAsync(
        Guid userId, Guid folderId, AddItemsToFolderBulkRequest request, CancellationToken ct = default)
    {
        var isOwner = await _folderRepo.ExistsByOwnerAsync(folderId, userId, ct);
        if (!isOwner)
            throw new ForbiddenException("Only the folder owner can add items to this folder.");

        var uniqueRequestIds = request.ItemIds.Distinct().ToList();
        var ownedItems = await _itemRepo.GetByIdsAndUserAsync(uniqueRequestIds, userId, ct);
        var ownedItemIds = ownedItems.Select(i => i.Id).ToHashSet();

        if (uniqueRequestIds.Any(id => !ownedItemIds.Contains(id)))
        {
            throw new ForbiddenException("One or more items do not belong to the current user or do not exist.");
        }

        // Get existing items in folder
        var existingItemFolders = await _folderRepo.GetItemFoldersAsync(request.ItemIds, folderId, ct);
        var existingItemIds = existingItemFolders.Select(i => i.ItemId).ToHashSet();

        var itemIdsToAdd = request.ItemIds.Where(id => !existingItemIds.Contains(id)).Distinct().ToList();
        if (!itemIdsToAdd.Any())
            return; // Nothing to add

        var maxPos = await _folderRepo.GetMaxItemPositionAsync(folderId, ct);
        
        var newFolders = itemIdsToAdd.Select((itemId, index) => new ItemFolder
        {
            ItemId = itemId,
            FolderId = folderId,
            Position = maxPos + 1 + index,
            AddedAt = DateTime.UtcNow
        }).ToList();

        await _folderRepo.AddItemsFolderAsync(newFolders, ct);
        await _folderRepo.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task RemoveItemFromFolderAsync(
        Guid userId, Guid folderId, Guid itemId, CancellationToken ct = default)
    {
        var isOwner = await _folderRepo.ExistsByOwnerAsync(folderId, userId, ct);
        if (!isOwner)
            throw new ForbiddenException("Only the folder owner can remove items from this folder.");

        var itemFolder = await _folderRepo.GetItemFolderAsync(itemId, folderId, ct)
            ?? throw new NotFoundException($"Item {itemId} is not in folder {folderId}.");

        _folderRepo.RemoveItemFolder(itemFolder);
        await _folderRepo.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task RemoveItemsFromFolderAsync(
        Guid userId, Guid folderId, RemoveItemsFromFolderBulkRequest request, CancellationToken ct = default)
    {
        var isOwner = await _folderRepo.ExistsByOwnerAsync(folderId, userId, ct);
        if (!isOwner)
            throw new ForbiddenException("Only the folder owner can remove items from this folder.");

        var itemFolders = await _folderRepo.GetItemFoldersAsync(request.ItemIds, folderId, ct);
        if (itemFolders.Any())
        {
            _folderRepo.RemoveItemsFolder(itemFolders);
            await _folderRepo.SaveChangesAsync(ct);
        }
    }

    // ─────────────────────────── Sharing methods ───────────────────────────

    /// <inheritdoc/>
    public async Task<FolderShareDto> InviteShareAsync(
        Guid folderId, Guid requestingUserId, InviteFolderShareRequest request, CancellationToken ct = default)
    {
        // 1. Validate input format
        await _inviteValidator.ValidateAndThrowAsync(request, ct);

        // 2. Kiểm tra folder tồn tại và caller là Owner
        var folder = await _folderRepo.GetByIdWithOwnerAsync(folderId, ct)
            ?? throw new NotFoundException(nameof(Folder), folderId);

        if (folder.OwnerId != requestingUserId)
            throw new ForbiddenException("Chỉ Owner mới được chia sẻ folder.");

        // 3. Không được share với chính mình
        if (request.FriendUserId == requestingUserId)
            throw new BusinessRuleException("Không thể chia sẻ folder với chính mình.");

        // 4. Kiểm tra FriendUserId là bạn bè đã accept (cả 2 chiều)
        var friendship = await _friendships.GetBetweenAsync(requestingUserId, request.FriendUserId, ct);
        if (friendship is null || friendship.Status != FriendshipStatus.Accepted)
            throw new BusinessRuleException("Chỉ có thể chia sẻ folder với bạn bè đã kết bạn.");

        // 5. Kiểm tra chưa share (kể cả pending)
        var alreadyShared = await _folderRepo.ShareExistsAsync(folderId, request.FriendUserId, ct);
        if (alreadyShared)
            throw new ConflictException("Folder đã được chia sẻ với người dùng này.");

        // 6. Parse permission
        if (!Enum.TryParse<SharePermission>(request.Permission, ignoreCase: true, out var permission))
            throw new BusinessRuleException($"Permission không hợp lệ: {request.Permission}");

        // 7. Tạo FolderShare (pending: AcceptedAt = null)
        var share = new FolderShare
        {
            Id = Guid.NewGuid(),
            FolderId = folderId,
            SharedWithUserId = request.FriendUserId,
            CreatedByUserId = requestingUserId,
            Permission = permission,
            CreatedAt = DateTime.UtcNow,
            AcceptedAt = null  // pending
        };

        await _folderRepo.AddShareAsync(share, ct);
        await _folderRepo.SaveChangesAsync(ct);

        // 8. Load lại với navigations để map DTO
        var saved = await _folderRepo.GetShareByIdAsync(share.Id, ct)!;

        // 9. Gửi notification cho người được mời (best-effort)
        await SendShareNotificationSafeAsync(
            saved!.SharedWithUserId,
            folder.Owner?.FullName ?? "Someone",
            folder.Name,
            ct);

        return MapShareToDto(saved!);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<FolderShareDto>> GetSharesForFolderAsync(
        Guid folderId, Guid requestingUserId, CancellationToken ct = default)
    {
        // Kiểm tra caller là Owner
        var isOwner = await _folderRepo.ExistsByOwnerAsync(folderId, requestingUserId, ct);
        if (!isOwner)
            throw new ForbiddenException("Chỉ Owner mới được xem danh sách chia sẻ.");

        var shares = await _folderRepo.GetSharesByFolderAsync(folderId, ct);
        return shares.Select(MapShareToDto).ToList().AsReadOnly();
    }

    /// <inheritdoc/>
    public async Task<FolderShareDto> UpdateShareRoleAsync(
        Guid folderId, Guid shareId, Guid requestingUserId, UpdateFolderShareRequest request, CancellationToken ct = default)
    {
        // 1. Validate input
        await _updateShareValidator.ValidateAndThrowAsync(request, ct);

        // 2. Kiểm tra caller là Owner của folder
        var isOwner = await _folderRepo.ExistsByOwnerAsync(folderId, requestingUserId, ct);
        if (!isOwner)
            throw new ForbiddenException("Chỉ Owner mới được thay đổi quyền chia sẻ.");

        // 3. Lấy share — phải thuộc folder này
        var share = await _folderRepo.GetShareByIdAsync(shareId, ct)
            ?? throw new NotFoundException(nameof(FolderShare), shareId);

        if (share.FolderId != folderId)
            throw new NotFoundException(nameof(FolderShare), shareId);

        // 4. Parse permission
        if (!Enum.TryParse<SharePermission>(request.Permission, ignoreCase: true, out var permission))
            throw new BusinessRuleException($"Permission không hợp lệ: {request.Permission}");

        // 5. Cập nhật
        share.Permission = permission;
        await _folderRepo.SaveChangesAsync(ct);

        return MapShareToDto(share);
    }

    /// <inheritdoc/>
    public async Task RevokeShareAsync(
        Guid folderId, Guid shareId, Guid requestingUserId, CancellationToken ct = default)
    {
        // Kiểm tra caller là Owner
        var isOwner = await _folderRepo.ExistsByOwnerAsync(folderId, requestingUserId, ct);
        if (!isOwner)
            throw new ForbiddenException("Chỉ Owner mới được thu hồi quyền chia sẻ.");

        var share = await _folderRepo.GetShareByIdAsync(shareId, ct)
            ?? throw new NotFoundException(nameof(FolderShare), shareId);

        if (share.FolderId != folderId)
            throw new NotFoundException(nameof(FolderShare), shareId);

        _folderRepo.RemoveShare(share);
        await _folderRepo.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SharedFolderDto>> GetFoldersSharedWithMeAsync(
        Guid userId, CancellationToken ct = default)
    {
        var shares = await _folderRepo.GetSharesForUserAsync(userId, ct);
        return shares.Select(fs => new SharedFolderDto(
            ShareId: fs.Id,
            FolderId: fs.FolderId,
            FolderName: fs.Folder.Name,
            OwnerUserId: fs.Folder.OwnerId,
            OwnerName: fs.Folder.Owner?.FullName ?? "Unknown",
            Permission: fs.Permission.ToString(),
            Status: fs.AcceptedAt.HasValue ? "Accepted" : "Pending",
            SharedAt: fs.CreatedAt
        )).ToList().AsReadOnly();
    }

    /// <inheritdoc/>
    public async Task<FolderShareDto> AcceptShareAsync(
        Guid shareId, Guid userId, CancellationToken ct = default)
    {
        var share = await _folderRepo.GetShareByIdAsync(shareId, ct)
            ?? throw new NotFoundException(nameof(FolderShare), shareId);

        // Chỉ người được share mới accept được
        if (share.SharedWithUserId != userId)
            throw new ForbiddenException("Chỉ người được mời mới có thể chấp nhận lời mời chia sẻ.");

        // Đã accept rồi
        if (share.AcceptedAt.HasValue)
            throw new ConflictException("Lời mời chia sẻ này đã được chấp nhận trước đó.");

        share.AcceptedAt = DateTime.UtcNow;
        await _folderRepo.SaveChangesAsync(ct);

        return MapShareToDto(share);
    }

    /// <inheritdoc/>
    public async Task DeclineShareAsync(
        Guid shareId, Guid userId, CancellationToken ct = default)
    {
        var share = await _folderRepo.GetShareByIdAsync(shareId, ct)
            ?? throw new NotFoundException(nameof(FolderShare), shareId);

        // Chỉ người được share mới decline được
        if (share.SharedWithUserId != userId)
            throw new ForbiddenException("Chỉ người được mời mới có thể từ chối lời mời chia sẻ.");

        // Decline = xoá row (không giữ trạng thái)
        _folderRepo.RemoveShare(share);
        await _folderRepo.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task LeaveFolderAsync(
        Guid folderId, Guid userId, CancellationToken ct = default)
    {
        var share = await _folderRepo.GetShareByFolderAndUserAsync(folderId, userId, ct)
            ?? throw new NotFoundException($"Không tìm thấy chia sẻ cho folder '{folderId}' và user '{userId}'");

        _folderRepo.RemoveShare(share);
        await _folderRepo.SaveChangesAsync(ct);
    }

    // ─────────────────────────── Private helpers ───────────────────────────

    /// <summary>Map Folder entity → FolderResponse DTO với computed fields.</summary>
    private static FolderResponse MapToDto(Folder folder, Guid currentUserId)
    {
        var isOwner = folder.OwnerId == currentUserId;

        // Permission: Owner → "Owner"; shared user → lấy từ FolderShares
        string permission;
        if (isOwner)
        {
            permission = "Owner";
        }
        else
        {
            var share = folder.FolderShares?
                .FirstOrDefault(fs => fs.SharedWithUserId == currentUserId);
            permission = share?.Permission.ToString() ?? SharePermission.Viewer.ToString();
        }

        return new FolderResponse(
            Id: folder.Id,
            Name: folder.Name,
            Color: folder.Color,
            Icon: folder.Icon,
            SortOrder: folder.SortOrder,
            IsArchived: folder.IsArchived,
            ItemCount: folder.ItemFolders?.Count ?? 0,
            IsOwner: isOwner,
            Permission: permission,
            OwnerName: folder.Owner?.FullName ?? "Unknown");
    }

    /// <summary>Map FolderShare entity → FolderShareDto.</summary>
    private static FolderShareDto MapShareToDto(FolderShare fs) => new(
        ShareId: fs.Id,
        FolderId: fs.FolderId,
        FolderName: fs.Folder?.Name ?? string.Empty,
        SharedWithUserId: fs.SharedWithUserId,
        SharedWithUserName: fs.SharedWithUser?.FullName ?? string.Empty,
        SharedWithUserAvatar: fs.SharedWithUser?.AvatarUrl,
        Permission: fs.Permission.ToString(),
        Status: fs.AcceptedAt.HasValue ? "Accepted" : "Pending",
        SharedAt: fs.CreatedAt);

    /// <summary>Gửi notification khi share invite — best-effort, không throw nếu lỗi.</summary>
    private async Task SendShareNotificationSafeAsync(
        Guid targetUserId, string ownerName, string folderName, CancellationToken ct)
    {
        try
        {
            await _notifications.CreateAndSendAsync(
                targetUserId,
                NotificationType.ShareInvite,
                $"{ownerName} đã chia sẻ folder '{folderName}' với bạn",
                $"{{\"from\":\"{ownerName}\",\"folder\":\"{folderName}\"}}",
                "/",
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gửi notification ShareInvite tới {UserId} thất bại", targetUserId);
        }
    }
}
