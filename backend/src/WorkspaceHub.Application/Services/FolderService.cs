using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Services;

/// <summary>
/// Business logic cho Folder CRUD.
/// Chứa toàn bộ logic: ownership check, computed fields (itemCount, isOwner, permission, ownerName).
/// Throw custom exception → middleware map sang status code (xem CONVENTIONS.md).
/// </summary>
public class FolderService : IFolderService
{
    private readonly IFolderRepository _folderRepo;
    private readonly IItemRepository _itemRepo;

    public FolderService(IFolderRepository folderRepo, IItemRepository itemRepo)
    {
        _folderRepo = folderRepo;
        _itemRepo = itemRepo;
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

    // ───────────────────────── Private helpers ─────────────────────────

    /// <summary>
    /// Map Folder entity → FolderResponse DTO với computed fields.
    /// </summary>
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
}
