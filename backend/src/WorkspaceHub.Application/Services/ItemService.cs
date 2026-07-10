using Microsoft.Extensions.Logging;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Services;

/// <summary>
/// Business logic cho Items listing.
/// Nhận/trả DTO, không trả entity ra ngoài (xem CONVENTIONS.md).
/// </summary>
public class ItemService : IItemService
{
    private readonly IItemRepository _itemRepo;
    private readonly IFolderRepository _folderRepo;
    private readonly IConnectionHealthChecker _healthChecker;
    private readonly ILogger<ItemService> _logger;

    public ItemService(
        IItemRepository itemRepo,
        IFolderRepository folderRepo,
        IConnectionHealthChecker healthChecker,
        ILogger<ItemService> logger)
    {
        _itemRepo = itemRepo;
        _folderRepo = folderRepo;
        _healthChecker = healthChecker;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<PagedResult<ItemResponse>> GetItemsAsync(
        Guid userId, GetItemsRequest request, CancellationToken ct = default)
    {
        // On-demand sync: check connections → auto-refresh → sync trước khi trả Items
        try
        {
            await _healthChecker.EnsureAllSyncedAsync(userId, ct);
        }
        catch (Exception ex)
        {
            // Sync fail KHÔNG chặn user xem items cũ — log warning và tiếp tục
            _logger.LogWarning(ex, "On-demand sync failed for user {UserId}, returning cached items.", userId);
        }

        // Clamp page/limit to safe ranges (validator should catch, but defense-in-depth)
        var page = Math.Max(1, request.Page);
        var limit = Math.Clamp(request.Limit, 1, 100);

        var types = request.Types;
        if ((types is null || types.Count == 0) && !string.IsNullOrWhiteSpace(request.ParticipantEmail))
            types = [ItemType.Email];

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

        var (items, totalCount, threadCounts) = await _itemRepo.GetPagedAsync(
            userId,
            request.FolderId,
            request.Statuses,
            types,
            request.IsImportant,
            request.Search?.Trim(),
            request.TagId,
            request.ProjectKey,
            request.GmailLabel,
            request.Assignee,
            request.ParticipantEmail?.Trim().ToLowerInvariant(),
            page,
            limit,
            ct);

        // Map entities → DTOs (kèm số message trong thread cho item Email đã gộp)
        var dtos = items.Select(i => MapToResponse(
            i,
            i.ThreadId != null && threadCounts.TryGetValue(i.ThreadId, out var c) ? c : 1))
            .ToList().AsReadOnly();

        return new PagedResult<ItemResponse>(dtos, totalCount, page, limit);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<JiraAssigneeDto>> GetTicketAssigneesAsync(Guid userId, CancellationToken ct = default)
    {
        var rows = await _itemRepo.GetTicketAssigneesAsync(userId, ct);
        return rows
            .Where(r => r.AccountId != null)
            .Select(r => new JiraAssigneeDto(r.AccountId!, r.DisplayName))
            .ToList()
            .AsReadOnly();
    }

    /// <inheritdoc/>
    public async Task<ItemResponse> UpdateStatusAsync(
        Guid userId, Guid itemId, UpdateItemStatusRequest request, CancellationToken ct = default)
    {
        var item = await _itemRepo.GetByIdAndUserAsync(itemId, userId, ct)
            ?? throw new NotFoundException(nameof(Item), itemId);

        item.Status = request.Status;

        _itemRepo.Update(item);
        await _itemRepo.SaveChangesAsync(ct);

        return MapToResponse(item);
    }

    /// <inheritdoc/>
    public async Task<ItemResponse> CreateNoteAsync(
        Guid userId, CreateNoteRequest request, CancellationToken ct = default)
    {
        var snippet = request.ContentMarkdown.Length > 200 
            ? request.ContentMarkdown.Substring(0, 197) + "..." 
            : request.ContentMarkdown;

        var metadata = System.Text.Json.JsonSerializer.Serialize(new { contentMarkdown = request.ContentMarkdown });

        var item = new Item
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ConnectionId = null,
            ExternalId = null,
            Type = ItemType.Note,
            Title = request.Title,
            Snippet = snippet,
            MetadataJson = metadata,
            OccurredAt = DateTime.UtcNow,
            Status = ItemStatus.Inbox,
            IsImportant = false,
            IsArchived = false
        };

        await _itemRepo.AddAsync(item, ct);

        if (request.FolderId.HasValue)
        {
            var folderId = request.FolderId.Value;
            var isOwner = await _folderRepo.ExistsByOwnerAsync(folderId, userId, ct);
            if (!isOwner)
                throw new ForbiddenException("Only the folder owner can add items to this folder.");

            var maxPos = await _folderRepo.GetMaxItemPositionAsync(folderId, ct);
            var itemFolder = new ItemFolder
            {
                ItemId = item.Id,
                FolderId = folderId,
                Position = maxPos + 1,
                AddedAt = DateTime.UtcNow
            };
            await _folderRepo.AddItemFolderAsync(itemFolder, ct);
        }

        await _itemRepo.SaveChangesAsync(ct);

        return MapToResponse(item);
    }

    /// <inheritdoc/>
    public async Task<ItemResponse> GetItemByIdAsync(Guid userId, Guid itemId, CancellationToken ct = default)
    {
        var item = await _itemRepo.GetByIdAndUserAsync(itemId, userId, ct)
            ?? throw new NotFoundException(nameof(Item), itemId);

        return MapToResponse(item);
    }

    /// <inheritdoc/>
    public async Task<ItemResponse> ToggleImportantAsync(Guid userId, Guid itemId, bool isImportant, CancellationToken ct = default)
    {
        var item = await _itemRepo.GetByIdAndUserAsync(itemId, userId, ct)
            ?? throw new NotFoundException(nameof(Item), itemId);

        item.IsImportant = isImportant;
        _itemRepo.Update(item);
        await _itemRepo.SaveChangesAsync(ct);

        return MapToResponse(item);
    }

    // ───────────────────────── Private helpers ─────────────────────────

    /// <summary>Map Item entity → ItemResponse DTO.</summary>
    private static ItemResponse MapToResponse(Item item, int threadCount = 1) => new(
        Id: item.Id,
        Type: item.Type,
        Title: item.Title,
        Snippet: item.Snippet,
        Status: item.Status,
        OccurredAt: item.OccurredAt,
        DueAt: item.DueAt,
        IsImportant: item.IsImportant,
        ExternalId: item.ExternalId,
        MetadataJson: item.MetadataJson,
        FolderIds: item.ItemFolders.Select(f => f.FolderId).ToList(),
        Tags: item.TagAssignments
            .Where(ta => ta.Tag != null)
            .Select(ta => new ItemTag(ta.Tag.Id, ta.Tag.Name, ta.Tag.Color))
            .ToList(),
        ConnectionId: item.ConnectionId,
        ThreadId: item.ThreadId,
        ThreadCount: threadCount);
}
