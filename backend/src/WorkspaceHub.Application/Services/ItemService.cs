using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.DTOs.Emails;
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
    private readonly IConnectionRepository _connectionRepo;
    private readonly ICalendarGateway _calendarGateway;
    private readonly ISendEmailService _sendEmailService;
    private readonly ILogger<ItemService> _logger;

    public ItemService(
        IItemRepository itemRepo,
        IFolderRepository folderRepo,
        IConnectionHealthChecker healthChecker,
        IConnectionRepository connectionRepo,
        ICalendarGateway calendarGateway,
        ISendEmailService sendEmailService,
        ILogger<ItemService> logger)
    {
        _itemRepo = itemRepo;
        _folderRepo = folderRepo;
        _healthChecker = healthChecker;
        _connectionRepo = connectionRepo;
        _calendarGateway = calendarGateway;
        _sendEmailService = sendEmailService;
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
        var limit = Math.Clamp(request.Limit, 1, 200);

        // Validate folder ownership and permission
        Guid? folderOwnerId = null;
        if (request.FolderId.HasValue)
        {
            var folderId = request.FolderId.Value;
            var isOwner = await _folderRepo.ExistsByOwnerAsync(folderId, userId, ct);
            if (!isOwner)
            {
                // Kiểm tra xem thư mục có được chia sẻ với user này và đã được chấp nhận (Accepted) chưa
                var share = await _folderRepo.GetShareByFolderAndUserAsync(folderId, userId, ct);
                if (share == null || !share.AcceptedAt.HasValue)
                {
                    // Trả danh sách trống nếu không có quyền
                    return new PagedResult<ItemResponse>(new List<ItemResponse>().AsReadOnly(), 0, page, limit);
                }

                var folder = await _folderRepo.GetByIdWithOwnerAsync(folderId, ct);
                if (folder != null)
                {
                    folderOwnerId = folder.OwnerId;
                }
            }
        }

        var targetOwnerId = folderOwnerId ?? userId;
        var (items, totalCount, threadCounts) = await _itemRepo.GetPagedAsync(
            targetOwnerId,
            request.FolderId,
            request.Statuses,
            request.Types,
            request.IsImportant,
            request.Search?.Trim(),
            request.TagIds,
            request.ProjectKey,
            request.GmailLabel,
            request.Assignee,
            request.ConnectionId,
            request.OccurredFrom,
            request.OccurredTo,
            request.DriveParentId,
            request.DriveKind,
            page,
            limit,
            ct);

        // Map entities → DTOs (kèm số message trong thread cho item Email đã gộp)
        var dtos = items.Select(i => MapToResponse(
            i,
            i.ThreadId != null && threadCounts.TryGetValue(i.ThreadId, out var c) ? c : 1,
            currentUserId: userId))
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
        var item = await _itemRepo.GetByIdAndUserAsync(itemId, userId, ct);
        if (item == null)
        {
            // Kiểm tra xem item có thuộc folder được chia sẻ với quyền Editor hay không
            var isEditor = await _folderRepo.IsItemSharedWithUserAsEditorAsync(itemId, userId, ct);
            if (isEditor)
            {
                item = await _itemRepo.GetByIdAsync(itemId, ct);
            }
        }

        if (item == null)
            await ThrowNoWriteAccessAsync(itemId, userId, ct);

        item.Status = request.Status;

        _itemRepo.Update(item);
        await _itemRepo.SaveChangesAsync(ct);

        return MapToResponse(item, currentUserId: userId);
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
        var item = await _itemRepo.GetByIdAndUserAsync(itemId, userId, ct);
        if (item == null)
        {
            // Item không thuộc user → thử quyền chia sẻ (ĐỌC, Viewer trở lên).
            var candidate = await _itemRepo.GetByIdAsync(itemId, ct);
            if (candidate != null && await CanReadSharedAsync(candidate, userId, ct))
                item = candidate;
        }

        if (item == null)
            throw new NotFoundException(nameof(Item), itemId);

        return MapToResponse(item, currentUserId: userId);
    }

    /// <inheritdoc/>
    public async Task<ItemResponse> ToggleImportantAsync(Guid userId, Guid itemId, bool isImportant, CancellationToken ct = default)
    {
        // Owner HOẶC shared-Editor (giống UpdateStatusAsync) — trước đây chỉ check owner nên
        // người được share quyền Editor xoá/đổi trạng thái được nhưng đánh dấu quan trọng lại 404.
        var item = await _itemRepo.GetByIdAndUserAsync(itemId, userId, ct);
        if (item == null && await _folderRepo.IsItemSharedWithUserAsEditorAsync(itemId, userId, ct))
            item = await _itemRepo.GetByIdAsync(itemId, ct);

        if (item == null)
            await ThrowNoWriteAccessAsync(itemId, userId, ct);

        item.IsImportant = isImportant;
        _itemRepo.Update(item);
        await _itemRepo.SaveChangesAsync(ct);

        return MapToResponse(item, currentUserId: userId);
    }

    /// <inheritdoc/>
    public async Task<CalendarEventDetailResponse> GetCalendarEventDetailAsync(Guid userId, Guid itemId, CancellationToken ct = default)
    {
        // Owner HOẶC người được chia sẻ — đây là thao tác ĐỌC nên Viewer cũng được xem chi tiết.
        var item = await _itemRepo.GetByIdAndUserAsync(itemId, userId, ct);
        if (item == null && await _folderRepo.IsItemSharedWithUserAsync(itemId, userId, ct))
            item = await _itemRepo.GetByIdAsync(itemId, ct);

        if (item == null)
            throw new NotFoundException(nameof(Item), itemId);

        if (item.Type != ItemType.Event)
            throw new BusinessRuleException("Item is not a calendar event.");

        if (item.ConnectionId == null)
            throw new BusinessRuleException("Event is not linked to any connection.");

        var conn = await _connectionRepo.GetByIdAsync(item.ConnectionId.Value, ct)
            ?? throw new NotFoundException("Connection", item.ConnectionId.Value);

        // Connection phải thuộc OWNER của item (không phải người đang gọi) — người được share
        // mượn connection của owner, hợp lệ vì share-check ở trên đã pass.
        if (conn.UserId != item.UserId)
            throw new ForbiddenException("Not your connection.");

        if (item.ExternalId == null)
            throw new BusinessRuleException("Event has no external ID.");

        var liveEvent = await _calendarGateway.GetEventAsync(conn, "primary", item.ExternalId, ct);

        string? organizerEmail = liveEvent.OrganizerEmail;
        string? organizerDisplayName = null;
        if (liveEvent.FullAttendees != null)
        {
            var org = liveEvent.FullAttendees.FirstOrDefault(a => a.Organizer);
            if (org != null)
            {
                organizerEmail = org.Email;
                organizerDisplayName = org.DisplayName;
            }
        }

        var attendeesDto = liveEvent.FullAttendees?
            .Select(a => new CalendarEventAttendeeDto(a.Email, a.DisplayName, a.ResponseStatus, a.Comment, a.Organizer))
            .ToList() ?? new List<CalendarEventAttendeeDto>();

        var isOrganizer = string.Equals(organizerEmail, conn.ProviderAccountId, StringComparison.OrdinalIgnoreCase);
        var canEdit = isOrganizer || liveEvent.GuestsCanModify == true;
        var canInviteOthers = isOrganizer || liveEvent.GuestsCanInviteOthers != false;
        var canSeeGuestList = isOrganizer || liveEvent.GuestsCanSeeOtherGuests != false;
        if (!canSeeGuestList)
        {
            attendeesDto = attendeesDto.Where(a =>
                a.Organizer ||
                string.Equals(a.Email, conn.ProviderAccountId, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var attachmentsDto = liveEvent.DriveAttachments?
            .Select(a => new CalendarDriveAttachmentDto(a.FileId, a.Title, a.MimeType, a.FileUrl))
            .ToList() ?? new List<CalendarDriveAttachmentDto>();

        var remindersDto = item.Reminders
            .Select(r => new EventReminderDto(r.Id, r.ReminderType, r.OffsetValue, r.OffsetUnit, r.TimeOfDay))
            .ToList();

        return new CalendarEventDetailResponse(
            Id: item.Id,
            Title: liveEvent.Summary ?? item.Title,
            Description: liveEvent.Description,
            Start: liveEvent.Start,
            End: liveEvent.End,
            AllDay: liveEvent.AllDay,
            Location: liveEvent.Location,
            MeetUrl: liveEvent.MeetUrl,
            HtmlLink: liveEvent.HtmlLink,
            OrganizerEmail: organizerEmail,
            OrganizerDisplayName: organizerDisplayName,
            Attendees: attendeesDto,
            DriveAttachments: attachmentsDto,
            OwningCalendarName: conn.ProviderAccountId,
            Reminders: remindersDto,
            Recurrence: liveEvent.Recurrence != null ? liveEvent.Recurrence.ToList() : new List<string>(),
            ICalUid: liveEvent.ICalUid,
            GuestsCanModify: liveEvent.GuestsCanModify ?? false,
            GuestsCanInviteOthers: liveEvent.GuestsCanInviteOthers ?? true,
            GuestsCanSeeOtherGuests: liveEvent.GuestsCanSeeOtherGuests ?? true,
            CanEdit: canEdit,
            CanInviteOthers: canInviteOthers,
            CanSeeGuestList: canSeeGuestList,
            IsOrganizer: isOrganizer
        );
    }

    /// <inheritdoc/>
    public async Task RsvpEventAsync(Guid userId, Guid itemId, RsvpRequest request, CancellationToken ct = default)
    {
        // RSVP = GHI lên lịch của owner → chỉ owner hoặc shared-Editor. Viewer nhận 403 rõ nghĩa.
        var item = await _itemRepo.GetByIdAndUserAsync(itemId, userId, ct);
        if (item == null && await _folderRepo.IsItemSharedWithUserAsEditorAsync(itemId, userId, ct))
            item = await _itemRepo.GetByIdAsync(itemId, ct);

        if (item == null)
            await ThrowNoWriteAccessAsync(itemId, userId, ct);

        if (item.Type != ItemType.Event)
            throw new BusinessRuleException("Item is not a calendar event.");

        if (item.ConnectionId == null)
            throw new BusinessRuleException("Event is not linked to any connection.");

        var conn = await _connectionRepo.GetByIdAsync(item.ConnectionId.Value, ct)
            ?? throw new NotFoundException("Connection", item.ConnectionId.Value);

        if (conn.UserId != item.UserId)
            throw new ForbiddenException("Not your connection.");

        if (item.ExternalId == null)
            throw new BusinessRuleException("Event has no external ID.");

        await _calendarGateway.RsvpEventAsync(conn, "primary", item.ExternalId, request.Response, request.Comment, ct);

        var liveEvent = await _calendarGateway.GetEventAsync(conn, "primary", item.ExternalId, ct);
        item.ETag = liveEvent.ETag;

        if (liveEvent.Attendees != null && !string.IsNullOrEmpty(item.MetadataJson))
        {
            try
            {
                var meta = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(item.MetadataJson);
                if (meta != null)
                {
                    meta["attendees"] = liveEvent.Attendees;
                    item.MetadataJson = System.Text.Json.JsonSerializer.Serialize(meta);
                }
            }
            catch { /* Ignore parse error */ }
        }

        _itemRepo.Update(item);
        await _itemRepo.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task SendEmailToGuestsAsync(Guid userId, Guid itemId, SendEmailToGuestsRequest request, CancellationToken ct = default)
    {
        var item = await _itemRepo.GetByIdAndUserAsync(itemId, userId, ct)
            ?? throw new NotFoundException(nameof(Item), itemId);

        if (item.Type != ItemType.Event)
            throw new BusinessRuleException("Item is not a calendar event.");

        var connections = await _connectionRepo.GetByUserIdAsync(userId, ct);
        var gmailConn = connections.FirstOrDefault(c => c.ServiceType == ServiceType.Gmail && c.Status == ConnectionStatus.Active);
        if (gmailConn == null)
            throw new BusinessRuleException("Bạn cần kết nối Gmail để gửi email mời khách.");

        var toEmails = request.RecipientEmails.Where(e => !string.IsNullOrEmpty(e)).Distinct().ToList();
        if (toEmails.Count == 0)
            throw new BusinessRuleException("Danh sách người nhận không được trống.");

        var sendRequest = new SendEmailRequest
        {
            ConnectionId = gmailConn.Id,
            To = toEmails,
            Subject = request.Subject,
            BodyHtml = request.BodyHtml
        };

        if (request.SendCopyToMe)
        {
            sendRequest.Cc.Add(gmailConn.ProviderAccountId);
        }

        await _sendEmailService.SendAsync(userId, sendRequest, ct);
    }

    /// <inheritdoc/>
    public async Task<CalendarEventDetailResponse> GetCalendarEventDetailAsync(Guid userId, Guid itemId, CancellationToken ct = default)
    {
        var item = await _itemRepo.GetByIdAndUserAsync(itemId, userId, ct)
            ?? throw new NotFoundException(nameof(Item), itemId);

        if (item.Type != ItemType.Event)
            throw new BusinessRuleException("Item is not a calendar event.");

        if (item.ConnectionId == null)
            throw new BusinessRuleException("Event is not linked to any connection.");

        var conn = await _connectionRepo.GetByIdAsync(item.ConnectionId.Value, ct)
            ?? throw new NotFoundException("Connection", item.ConnectionId.Value);

        if (conn.UserId != userId)
            throw new ForbiddenException("Not your connection.");

        if (item.ExternalId == null)
            throw new BusinessRuleException("Event has no external ID.");

        var liveEvent = await _calendarGateway.GetEventAsync(conn, "primary", item.ExternalId, ct);

        string? organizerEmail = liveEvent.OrganizerEmail;
        string? organizerDisplayName = null;
        if (liveEvent.FullAttendees != null)
        {
            var org = liveEvent.FullAttendees.FirstOrDefault(a => a.Organizer);
            if (org != null)
            {
                organizerEmail = org.Email;
                organizerDisplayName = org.DisplayName;
            }
        }

        var attendeesDto = liveEvent.FullAttendees?
            .Select(a => new CalendarEventAttendeeDto(a.Email, a.DisplayName, a.ResponseStatus, a.Comment, a.Organizer))
            .ToList() ?? new List<CalendarEventAttendeeDto>();

        var isOrganizer = string.Equals(organizerEmail, conn.ProviderAccountId, StringComparison.OrdinalIgnoreCase);
        var canEdit = isOrganizer || liveEvent.GuestsCanModify == true;
        var canInviteOthers = isOrganizer || liveEvent.GuestsCanInviteOthers != false;
        var canSeeGuestList = isOrganizer || liveEvent.GuestsCanSeeOtherGuests != false;
        if (!canSeeGuestList)
        {
            attendeesDto = attendeesDto.Where(a =>
                a.Organizer ||
                string.Equals(a.Email, conn.ProviderAccountId, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var attachmentsDto = liveEvent.DriveAttachments?
            .Select(a => new CalendarDriveAttachmentDto(a.FileId, a.Title, a.MimeType, a.FileUrl))
            .ToList() ?? new List<CalendarDriveAttachmentDto>();

        var remindersDto = item.Reminders
            .Select(r => new EventReminderDto(r.Id, r.ReminderType, r.OffsetValue, r.OffsetUnit, r.TimeOfDay))
            .ToList();

        return new CalendarEventDetailResponse(
            Id: item.Id,
            Title: liveEvent.Summary ?? item.Title,
            Description: liveEvent.Description,
            Start: liveEvent.Start,
            End: liveEvent.End,
            AllDay: liveEvent.AllDay,
            Location: liveEvent.Location,
            MeetUrl: liveEvent.MeetUrl,
            HtmlLink: liveEvent.HtmlLink,
            OrganizerEmail: organizerEmail,
            OrganizerDisplayName: organizerDisplayName,
            Attendees: attendeesDto,
            DriveAttachments: attachmentsDto,
            OwningCalendarName: conn.ProviderAccountId,
            Reminders: remindersDto,
            Recurrence: liveEvent.Recurrence != null ? liveEvent.Recurrence.ToList() : new List<string>(),
            ICalUid: liveEvent.ICalUid,
            GuestsCanModify: liveEvent.GuestsCanModify ?? false,
            GuestsCanInviteOthers: liveEvent.GuestsCanInviteOthers ?? true,
            GuestsCanSeeOtherGuests: liveEvent.GuestsCanSeeOtherGuests ?? true,
            CanEdit: canEdit,
            CanInviteOthers: canInviteOthers,
            CanSeeGuestList: canSeeGuestList,
            IsOrganizer: isOrganizer
        );
    }

    /// <inheritdoc/>
    public async Task RsvpEventAsync(Guid userId, Guid itemId, RsvpRequest request, CancellationToken ct = default)
    {
        var item = await _itemRepo.GetByIdAndUserAsync(itemId, userId, ct)
            ?? throw new NotFoundException(nameof(Item), itemId);

        if (item.Type != ItemType.Event)
            throw new BusinessRuleException("Item is not a calendar event.");

        if (item.ConnectionId == null)
            throw new BusinessRuleException("Event is not linked to any connection.");

        var conn = await _connectionRepo.GetByIdAsync(item.ConnectionId.Value, ct)
            ?? throw new NotFoundException("Connection", item.ConnectionId.Value);

        if (conn.UserId != userId)
            throw new ForbiddenException("Not your connection.");

        if (item.ExternalId == null)
            throw new BusinessRuleException("Event has no external ID.");

        await _calendarGateway.RsvpEventAsync(conn, "primary", item.ExternalId, request.Response, request.Comment, ct);

        var liveEvent = await _calendarGateway.GetEventAsync(conn, "primary", item.ExternalId, ct);
        item.ETag = liveEvent.ETag;

        if (liveEvent.Attendees != null && !string.IsNullOrEmpty(item.MetadataJson))
        {
            try
            {
                var meta = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(item.MetadataJson);
                if (meta != null)
                {
                    meta["attendees"] = liveEvent.Attendees;
                    item.MetadataJson = System.Text.Json.JsonSerializer.Serialize(meta);
                }
            }
            catch { /* Ignore parse error */ }
        }

        _itemRepo.Update(item);
        await _itemRepo.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task SendEmailToGuestsAsync(Guid userId, Guid itemId, SendEmailToGuestsRequest request, CancellationToken ct = default)
    {
        var item = await _itemRepo.GetByIdAndUserAsync(itemId, userId, ct)
            ?? throw new NotFoundException(nameof(Item), itemId);

        if (item.Type != ItemType.Event)
            throw new BusinessRuleException("Item is not a calendar event.");

        var connections = await _connectionRepo.GetByUserIdAsync(userId, ct);
        var gmailConn = connections.FirstOrDefault(c => c.ServiceType == ServiceType.Gmail && c.Status == ConnectionStatus.Active);
        if (gmailConn == null)
            throw new BusinessRuleException("Bạn cần kết nối Gmail để gửi email mời khách.");

        var toEmails = request.RecipientEmails.Where(e => !string.IsNullOrEmpty(e)).Distinct().ToList();
        if (toEmails.Count == 0)
            throw new BusinessRuleException("Danh sách người nhận không được trống.");

        var sendRequest = new SendEmailRequest
        {
            ConnectionId = gmailConn.Id,
            To = toEmails,
            Subject = request.Subject,
            BodyHtml = request.BodyHtml
        };

        if (request.SendCopyToMe)
        {
            sendRequest.Cc.Add(gmailConn.ProviderAccountId);
        }

        await _sendEmailService.SendAsync(userId, sendRequest, ct);
    }

    // ───────────────────────── Private helpers ─────────────────────────

    /// <summary>
    /// User có quyền ĐỌC item qua chia sẻ không? Đúng khi: item nằm trực tiếp trong folder được chia sẻ
    /// (có junction), HOẶC item là file/folder Drive con của folder Drive được share — con không có
    /// junction riêng (thiết kế parent-only) nên xét theo connection được share với user.
    /// </summary>
    private async Task<bool> CanReadSharedAsync(Item item, Guid userId, CancellationToken ct)
    {
        if (await _folderRepo.IsItemSharedWithUserAsync(item.Id, userId, ct))
            return true;

        return item.Type == ItemType.File
               && item.ConnectionId.HasValue
               && await _folderRepo.IsConnectionSharedWithUserAsync(item.ConnectionId.Value, userId, ct);
    }

    /// <summary>
    /// Ném lỗi khi user KHÔNG có quyền GHI lên item. Phân biệt 2 tình huống để message dễ hiểu:
    /// <list type="bullet">
    /// <item>Item nằm trong folder được chia sẻ nhưng user chỉ có quyền <b>Viewer</b> → 403 kèm
    /// lời giải thích (trước đây trả 404 "Item with id '...' was not found" — lộ GUID, khó hiểu).</item>
    /// <item>Ngược lại (item không tồn tại / không được chia sẻ) → 404 như cũ.</item>
    /// </list>
    /// </summary>
    [DoesNotReturn]
    private async Task ThrowNoWriteAccessAsync(Guid itemId, Guid userId, CancellationToken ct)
    {
        var isViewer = await _folderRepo.IsItemSharedWithUserAsync(itemId, userId, ct);
        if (isViewer)
            throw new ForbiddenException(
                "Bạn chỉ có quyền xem mục này trong thư mục được chia sẻ. Hãy yêu cầu chủ sở hữu cấp quyền chỉnh sửa.");

        throw new NotFoundException(nameof(Item), itemId);
    }

    /// <summary>
    /// Map Item entity → ItemResponse DTO.
    /// <paramref name="currentUserId"/>: truyền vào để tính <c>IsOwner</c> (item của chính user hay
    /// của người khác xem qua folder chia sẻ). Bỏ trống → coi như owner (giữ hành vi cũ).
    /// </summary>
    private static ItemResponse MapToResponse(Item item, int threadCount = 1, Guid? currentUserId = null) => new(
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
        // Tag là nhãn PRIVATE: item trong folder chia sẻ có thể mang tag của NHIỀU user khác nhau.
        // Chỉ trả tag của chính người đang xem, nếu không A sẽ thấy tag riêng của B và ngược lại.
        Tags: item.TagAssignments
            .Where(ta => ta.Tag != null && (currentUserId == null || ta.Tag.UserId == currentUserId.Value))
            .Select(ta => new ItemTag(ta.Tag.Id, ta.Tag.Name, ta.Tag.Color))
            .ToList(),
        ConnectionId: item.ConnectionId,
        ThreadId: item.ThreadId,
        ThreadCount: threadCount,
        IsOwner: currentUserId == null || item.UserId == currentUserId.Value);
}
