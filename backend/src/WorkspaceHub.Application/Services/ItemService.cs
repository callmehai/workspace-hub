using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Services;

/// <summary>
/// Business logic cho Items listing.
/// Nhận/trả DTO, không trả entity ra ngoài (xem CONVENTIONS.md).
/// </summary>
public class ItemService : IItemService
{
    private readonly IItemRepository _itemRepo;
    private readonly IFolderRepository _folderRepo;

    public ItemService(IItemRepository itemRepo, IFolderRepository folderRepo)
    {
        _itemRepo = itemRepo;
        _folderRepo = folderRepo;
    }

    /// <inheritdoc/>
    public async Task<PagedResult<ItemResponse>> GetItemsAsync(
        Guid userId, GetItemsRequest request, CancellationToken ct = default)
    {
        // Clamp page/limit to safe ranges (validator should catch, but defense-in-depth)
        var page = Math.Max(1, request.Page);
        var limit = Math.Clamp(request.Limit, 1, 100);

        // Validate folder ownership
        if (request.FolderId.HasValue)
        {
            var isOwner = await _folderRepo.ExistsByOwnerAsync(request.FolderId.Value, userId, ct);
            // TODO: khi shared folder được implement, mở rộng check này để include viewer access
            if (!isOwner)
            {
                // Return empty if folder doesn't exist or belongs to another user
                return new PagedResult<ItemResponse>(new List<ItemResponse>().AsReadOnly(), 0, page, limit);
            }
        }

        var (items, totalCount) = await _itemRepo.GetPagedAsync(
            userId,
            request.FolderId,
            request.Status,
            request.Type,
            request.IsImportant,
            request.Search?.Trim(),
            page,
            limit,
            ct);

        // Map entities → DTOs
        var dtos = items.Select(MapToResponse).ToList().AsReadOnly();

        return new PagedResult<ItemResponse>(dtos, totalCount, page, limit);
    }

    // ───────────────────────── Private helpers ─────────────────────────

    /// <summary>Map Item entity → ItemResponse DTO.</summary>
    private static ItemResponse MapToResponse(Item item) => new(
        Id: item.Id,
        Type: item.Type,
        Title: item.Title,
        Snippet: item.Snippet,
        Status: item.Status,
        OccurredAt: item.OccurredAt,
        DueAt: item.DueAt,
        IsImportant: item.IsImportant,
        ExternalId: item.ExternalId);
}
