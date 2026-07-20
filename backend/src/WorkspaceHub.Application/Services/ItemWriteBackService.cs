using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Mapping;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Services;

public class ItemWriteBackService : IItemWriteBackService
{
    private readonly IItemRepository _items;
    private readonly IConnectionRepository _connections;
    private readonly IFolderRepository _folders;
    private readonly IWriteBackGuard _guard;
    private readonly IGmailGateway _gmailGateway;
    private readonly ICalendarGateway _calendarGateway;
    private readonly IDriveGateway _driveGateway;
    private readonly IJiraGateway _jiraGateway;
    private readonly IJiraItemMapper _jiraMapper;
    private readonly ICalendarInvitationService? _calendarInvitations;

    public ItemWriteBackService(
        IItemRepository items,
        IConnectionRepository connections,
        IFolderRepository folders,
        IWriteBackGuard guard,
        IGmailGateway gmailGateway,
        ICalendarGateway calendarGateway,
        IDriveGateway driveGateway,
        IJiraGateway jiraGateway,
        IJiraItemMapper jiraMapper,
        ICalendarInvitationService? calendarInvitations = null)
    {
        _items = items;
        _connections = connections;
        _folders = folders;
        _guard = guard;
        _gmailGateway = gmailGateway;
        _calendarGateway = calendarGateway;
        _driveGateway = driveGateway;
        _jiraGateway = jiraGateway;
        _jiraMapper = jiraMapper;
        _calendarInvitations = calendarInvitations;
    }

    public ItemWriteBackService(
        IItemRepository items,
        IConnectionRepository connections,
        IWriteBackGuard guard,
        IGmailGateway gmailGateway,
        ICalendarGateway calendarGateway,
        IDriveGateway driveGateway,
        IJiraGateway jiraGateway,
        IJiraItemMapper jiraMapper)
        : this(items, connections, null!, guard, gmailGateway, calendarGateway, driveGateway, jiraGateway, jiraMapper)
    {
    }

    /// <summary>
    /// Ném lỗi khi user KHÔNG có quyền GHI lên item. Nếu item nằm trong folder được chia sẻ nhưng
    /// user chỉ là <b>Viewer</b> → 403 kèm giải thích, thay vì 404 "Item with id '...' was not found"
    /// (lộ GUID, người dùng không hiểu vì sao thao tác thất bại).
    /// </summary>
    [DoesNotReturn]
    private async Task ThrowNoWriteAccessAsync(Guid itemId, Guid userId, CancellationToken ct)
    {
        if (_folders != null && await _folders.IsItemSharedWithUserAsync(itemId, userId, ct))
            throw new ForbiddenException(
                "You only have view access to this item in a shared folder.", ErrorCodes.SharedViewerReadOnly);

        throw new NotFoundException("Item", itemId);
    }

    private async Task<Connection> GetConnectionAsync(Guid? connectionId, CancellationToken ct)
    {
        if (connectionId == null) throw new BusinessRuleException("Item is not linked to any connection.");
        var conn = await _connections.GetByIdAsync(connectionId.Value, ct);
        if (conn == null) throw new NotFoundException("Connection", connectionId.Value);
        return conn;
    }

    public async Task<ItemResponse> PatchItemAsync(Guid itemId, Guid userId, PatchItemRequest payload, CancellationToken ct = default)
    {
        var item = await _items.GetByIdAndUserAsync(itemId, userId, ct);
        if (item == null && _folders != null)
        {
            var isEditor = await _folders.IsItemSharedWithUserAsEditorAsync(itemId, userId, ct);
            if (isEditor)
            {
                item = await _items.GetByIdAsync(itemId, ct);
            }
        }
        if (item == null) await ThrowNoWriteAccessAsync(itemId, userId, ct);

        var conn = await GetConnectionAsync(item.ConnectionId, ct);

        // Jira (Type=Ticket) — luồng riêng: nội dung sửa được, status qua transition, version-token = fields.updated.
        if (item.Type == ItemType.Ticket)
            return await PatchTicketAsync(item, conn, payload, ct);

        if (item.Type == ItemType.Email && conn.ServiceType != ServiceType.Gmail) throw new BusinessRuleException("Connection type mismatch for Gmail.");
        if (item.Type == ItemType.Event && conn.ServiceType != ServiceType.GCal) throw new BusinessRuleException("Connection type mismatch for Calendar.");
        if (item.Type == ItemType.File && conn.ServiceType != ServiceType.Drive) throw new BusinessRuleException("Connection type mismatch for Drive.");

        if (item.ExternalId == null) throw new BusinessRuleException("Item has no external ID.");

        // belt-and-suspenders: validator already enforces this check, but keeping it for defense-in-depth.
        if (!payload.IsUnread.HasValue && !payload.IsStarred.HasValue && payload.AddLabels == null && payload.RemoveLabels == null &&
            !payload.IsTrashed.HasValue && payload.Title == null && payload.Start == null &&
            payload.End == null && payload.Location == null && payload.Attendees == null &&
            payload.Name == null && payload.Description == null &&
            payload.DriveItemIds == null && !payload.AllDay.HasValue && payload.Reminders == null &&
            payload.Recurrence == null && !payload.GuestsCanModify.HasValue &&
            !payload.GuestsCanInviteOthers.HasValue && !payload.GuestsCanSeeOtherGuests.HasValue)
        {
            throw new BusinessRuleException("No fields provided for update.");
        }

        string? providerEtag = null;
        CalendarEvent? liveCalendarEvent = null;
        switch (item.Type)
        {
            case ItemType.Email:
                providerEtag = await _gmailGateway.GetMessageETagAsync(conn, item.ExternalId, ct);
                break;
            case ItemType.Event:
                liveCalendarEvent = await _calendarGateway.GetEventAsync(conn, "primary", item.ExternalId, ct);
                providerEtag = liveCalendarEvent.ETag;
                break;
            case ItemType.File:
                var file = await _driveGateway.GetFileAsync(conn, item.ExternalId, ct);
                providerEtag = file.ETag;
                break;
            default:
                throw new BusinessRuleException("Unsupported item type for writeback.");
        }

        // Gmail ETag (HistoryId) changes constantly when labels are modified from external sources.
        // This causes frequent false-positive 409 conflicts during write-back (e.g. read/unread/star toggle).
        // Therefore, we bypass conflict checking (EnsureNoConflict) for emails.
        if (item.Type != ItemType.Email)
        {
            _guard.EnsureNoConflict(item.ETag, providerEtag);
        }

        string? newETag = null;

        switch (item.Type)
        {
            case ItemType.Email:
                if (payload.Title != null || payload.Start != null || payload.End != null || payload.Location != null || payload.Attendees != null || payload.Name != null || payload.SendUpdates.HasValue)
                    throw new BusinessRuleException("Invalid fields for Email writeback.");

                var addLabels = new List<string>();
                var removeLabels = new List<string>();

                bool isUnreadChanged = payload.IsUnread.HasValue;
                bool isStarredChanged = payload.IsStarred.HasValue;

                if (isUnreadChanged)
                {
                    if (payload.IsUnread!.Value) addLabels.Add("UNREAD");
                    else removeLabels.Add("UNREAD");
                }
                
                if (isStarredChanged)
                {
                    if (payload.IsStarred!.Value) addLabels.Add("STARRED");
                    else removeLabels.Add("STARRED");
                }
                
                if (payload.AddLabels != null) addLabels.AddRange(payload.AddLabels);
                if (payload.RemoveLabels != null) removeLabels.AddRange(payload.RemoveLabels);

                if (addLabels.Any() || removeLabels.Any())
                {
                    newETag = await _gmailGateway.ModifyMessageAsync(conn, item.ExternalId, addLabels, removeLabels, ct);
                }

                if (payload.IsTrashed.HasValue)
                {
                    if (payload.IsTrashed.Value)
                        newETag = await _gmailGateway.TrashMessageAsync(conn, item.ExternalId, ct);
                    else
                        newETag = await _gmailGateway.UntrashMessageAsync(conn, item.ExternalId, ct);
                }
                
                var metaDictEmail = string.IsNullOrEmpty(item.MetadataJson) ? new Dictionary<string, object>() : JsonSerializer.Deserialize<Dictionary<string, object>>(item.MetadataJson) ?? new Dictionary<string, object>();
                if (isUnreadChanged) metaDictEmail["isUnread"] = payload.IsUnread!.Value;
                if (isStarredChanged) metaDictEmail["isStarred"] = payload.IsStarred!.Value;

                // Đồng bộ luôn mảng `labels` trong metadata với thay đổi vừa ghi lên Gmail.
                // Nếu chỉ set field isUnread/isStarred mà không sửa labels → refetch từ DB sẽ
                // "hồi sinh" nhãn cũ (vd UNREAD) làm UI hiện lại sai sau khi đã đọc (bug desync).
                if (addLabels.Any() || removeLabels.Any())
                {
                    var labels = new List<string>();
                    if (metaDictEmail.TryGetValue("labels", out var lv) && lv is JsonElement je && je.ValueKind == JsonValueKind.Array)
                        labels = je.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(s => s.Length > 0).ToList();
                    labels.RemoveAll(l => removeLabels.Contains(l));
                    foreach (var al in addLabels)
                        if (!labels.Contains(al)) labels.Add(al);
                    metaDictEmail["labels"] = labels;
                }

                item.MetadataJson = JsonSerializer.Serialize(metaDictEmail);

                break;

            case ItemType.Event:
                if (payload.IsUnread != null || payload.IsStarred != null || payload.AddLabels != null || payload.RemoveLabels != null || payload.IsTrashed != null || payload.Name != null)
                    throw new BusinessRuleException("Invalid fields for Event writeback.");

                var currentMeta = ParseMetadataDict(item.MetadataJson);
                var organizerEmail = liveCalendarEvent?.OrganizerEmail ?? ReadMetaString(currentMeta, "organizerEmail") ?? conn.ProviderAccountId;
                var isOrganizer = string.Equals(organizerEmail, conn.ProviderAccountId, StringComparison.OrdinalIgnoreCase);
                if (!isOrganizer)
                {
                    var guestsCanModify = liveCalendarEvent?.GuestsCanModify ?? ReadMetaBool(currentMeta, "guestsCanModify");
                    var guestsCanInviteOthers = liveCalendarEvent?.GuestsCanInviteOthers ?? ReadMetaBool(currentMeta, "guestsCanInviteOthers", true);
                    if (!guestsCanModify)
                        throw new ForbiddenException("Organizer does not allow guests to modify this event.");
                    if (payload.Attendees != null && !guestsCanInviteOthers)
                        throw new ForbiddenException("Organizer does not allow guests to invite other people.");
                    if (payload.GuestsCanModify.HasValue || payload.GuestsCanInviteOthers.HasValue || payload.GuestsCanSeeOtherGuests.HasValue)
                        throw new ForbiddenException("Only the organizer can change guest permissions.");
                }
                var existingAllDay = ReadMetaAllDay(currentMeta);
                var effectiveAllDay = payload.AllDay ?? existingAllDay;

                var timeChanged = payload.Start.HasValue || payload.End.HasValue || payload.AllDay.HasValue;
                DateTimeOffset? effectiveStart = payload.Start ?? (timeChanged ? ReadEventStart(currentMeta, item) : null);
                DateTimeOffset? effectiveEnd = payload.End ?? (timeChanged ? ReadEventEnd(currentMeta, item) : null);

                IReadOnlyList<CalendarDriveAttachment>? driveAttachments = null;
                if (payload.DriveItemIds != null)
                {
                    if (payload.DriveItemIds.Count > 0)
                    {
                        var driveItems = await _items.GetByIdsAndUserAsync(payload.DriveItemIds, userId, ct);
                        driveAttachments = driveItems
                            .Where(di => di.ExternalId != null)
                            .Select(di =>
                            {
                                var dm = ParseMetadataDict(di.MetadataJson);
                                var mt = ReadMetaString(dm, "mimeType");
                                var url = ReadMetaString(dm, "webViewLink");
                                return new CalendarDriveAttachment(di.ExternalId!, di.Title, mt, url);
                            })
                            .ToList();
                    }
                    else
                    {
                        driveAttachments = Array.Empty<CalendarDriveAttachment>();
                    }
                }

                var evDto = new CalendarEvent(
                    item.ExternalId,
                    providerEtag,
                    payload.Title,
                    payload.Description,
                    effectiveStart,
                    effectiveEnd,
                    payload.Location,
                    payload.Attendees,
                    timeChanged ? effectiveAllDay : existingAllDay,
                    driveAttachments,
                    null, // fullAttendees (only used for read)
                    null, // meetUrl (only used for read)
                    null, // htmlLink (only used for read)
                    MapToGoogleReminders(payload.Reminders, timeChanged ? effectiveAllDay : existingAllDay),
                    payload.Recurrence,
                    OrganizerEmail: null,
                    SelfResponseStatus: null,
                    ICalUid: null,
                    GuestsCanModify: payload.GuestsCanModify,
                    GuestsCanInviteOthers: payload.GuestsCanInviteOthers,
                    GuestsCanSeeOtherGuests: payload.GuestsCanSeeOtherGuests,
                    SendUpdates: payload.SendUpdates ?? true
                );

                var updatedEvent = await _calendarGateway.UpdateEventAsync(conn, "primary", item.ExternalId, evDto, ct);
                newETag = updatedEvent.ETag;
                item.Title = updatedEvent.Summary ?? item.Title;
                if (updatedEvent.Description != null) item.Snippet = updatedEvent.Description;

                if (updatedEvent.Start.HasValue)
                    item.OccurredAt = updatedEvent.Start.Value.UtcDateTime;
                if (updatedEvent.End.HasValue)
                    item.DueAt = updatedEvent.End.Value.UtcDateTime;

                var metaDictEvent = ParseMetadataDict(item.MetadataJson);
                if (updatedEvent.Location != null) metaDictEvent["location"] = updatedEvent.Location;
                if (payload.Attendees != null)
                {
                    if (updatedEvent.Attendees is { Count: > 0 })
                        metaDictEvent["attendees"] = updatedEvent.Attendees;
                    else
                        metaDictEvent.Remove("attendees");
                }
                else if (updatedEvent.Attendees is { Count: > 0 })
                {
                    metaDictEvent["attendees"] = updatedEvent.Attendees;
                }
                if (payload.Description != null) metaDictEvent["description"] = payload.Description;
                if (updatedEvent.Recurrence != null && updatedEvent.Recurrence.Count > 0)
                    metaDictEvent["recurrence"] = updatedEvent.Recurrence;
                else
                    metaDictEvent.Remove("recurrence");
                metaDictEvent["organizerEmail"] = updatedEvent.OrganizerEmail ?? conn.ProviderAccountId;
                metaDictEvent["selfResponseStatus"] = updatedEvent.SelfResponseStatus ?? "accepted";
                if (!string.IsNullOrWhiteSpace(updatedEvent.ICalUid)) metaDictEvent["iCalUid"] = updatedEvent.ICalUid;
                metaDictEvent["guestsCanModify"] = updatedEvent.GuestsCanModify ?? false;
                metaDictEvent["guestsCanInviteOthers"] = updatedEvent.GuestsCanInviteOthers ?? true;
                metaDictEvent["guestsCanSeeOtherGuests"] = updatedEvent.GuestsCanSeeOtherGuests ?? true;
                if (payload.DriveItemIds != null)
                {
                    metaDictEvent["driveItemIds"] = payload.DriveItemIds;
                    if (driveAttachments != null && driveAttachments.Count > 0)
                    {
                        metaDictEvent["driveAttachments"] = ToDriveAttachmentMetadata(driveAttachments);
                    }
                    else
                    {
                        metaDictEvent.Remove("driveAttachments");
                    }
                }

                if (timeChanged || updatedEvent.Start.HasValue)
                {
                    var allDayForMeta = updatedEvent.AllDay || effectiveAllDay;
                    if (allDayForMeta)
                    {
                        metaDictEvent["allDay"] = true;
                        if (updatedEvent.Start.HasValue)
                            metaDictEvent["start"] = updatedEvent.Start.Value.ToString("yyyy-MM-dd");
                        if (updatedEvent.End.HasValue)
                            metaDictEvent["end"] = updatedEvent.End.Value.ToString("yyyy-MM-dd");
                    }
                    else
                    {
                        metaDictEvent.Remove("allDay");
                        if (updatedEvent.Start.HasValue)
                            metaDictEvent["start"] = updatedEvent.Start.Value.UtcDateTime.ToString("o");
                        if (updatedEvent.End.HasValue)
                            metaDictEvent["end"] = updatedEvent.End.Value.UtcDateTime.ToString("o");
                    }
                }

                if (payload.Reminders != null)
                {
                    item.Reminders.Clear();
                    foreach (var r in payload.Reminders)
                    {
                        item.Reminders.Add(new EventReminder
                        {
                            ReminderType = NormalizeReminderType(r.ReminderType),
                            OffsetValue = r.OffsetValue,
                            OffsetUnit = r.OffsetUnit,
                            TimeOfDay = r.TimeOfDay,
                            IsSent = false
                        });
                    }
                }

                item.MetadataJson = JsonSerializer.Serialize(metaDictEvent);
                break;

            case ItemType.File:
                if (payload.IsUnread != null || payload.IsStarred != null || payload.AddLabels != null || payload.RemoveLabels != null || payload.Title != null || payload.Start != null || payload.End != null || payload.Location != null || payload.Attendees != null || payload.SendUpdates.HasValue)
                    throw new BusinessRuleException("Invalid fields for File writeback.");
                    
                // Thiết kế: Chấp nhận rủi ro partial write nếu update Name thành công nhưng Trash thất bại.
                // Sync sau đó sẽ tự fix state.
                if (payload.Name != null)
                {
                    var updatedFile = await _driveGateway.UpdateFileAsync(conn, item.ExternalId, payload.Name, ct);
                    newETag = updatedFile.ETag;
                    item.Title = updatedFile.Name ?? "Untitled";
                }
                if (payload.IsTrashed.HasValue)
                {
                    if (payload.IsTrashed.Value)
                    {
                        var f = await _driveGateway.TrashFileAsync(conn, item.ExternalId, ct);
                        newETag = f.ETag;
                    }
                    else
                    {
                        var f = await _driveGateway.UntrashFileAsync(conn, item.ExternalId, ct);
                        newETag = f.ETag;
                    }
                }
                break;
        }

        if (newETag != null) item.ETag = newETag;
        
        await _items.SaveChangesAsync(ct);
        if (_calendarInvitations != null && item.Type == ItemType.Event && item.ConnectionId != null && item.ExternalId != null)
        {
            var meta = ParseMetadataDict(item.MetadataJson);
            var organizer = ReadMetaString(meta, "organizerEmail");
            if (string.IsNullOrWhiteSpace(organizer) || string.Equals(organizer, conn.ProviderAccountId, StringComparison.OrdinalIgnoreCase))
            {
                var live = await _calendarGateway.GetEventAsync(conn, "primary", item.ExternalId, ct);
                await _calendarInvitations.ReconcileOrganizerEventAsync(item, live, ct);
            }
        }
        return new ItemResponse(item.Id, item.Type, item.Title, item.Snippet, item.Status, item.OccurredAt, item.DueAt, item.IsImportant, item.ExternalId, item.MetadataJson, item.ItemFolders.Select(f => f.FolderId).ToList(), item.TagAssignments.Where(ta => ta.Tag != null).Select(ta => new ItemTag(ta.Tag.Id, ta.Tag.Name, ta.Tag.Color)).ToList(), item.ConnectionId);
    }

    public async Task<ItemResponse> CreateEventAsync(Guid userId, CreateEventRequest payload, CancellationToken ct = default)
    {
        var conn = await _connections.GetByIdAsync(payload.ConnectionId, ct);
        if (conn == null) throw new NotFoundException("Connection", payload.ConnectionId);
        if (conn.UserId != userId) throw new ForbiddenException("Not your connection.");
        if (conn.ServiceType != ServiceType.GCal) throw new BusinessRuleException("Connection is not for Calendar.");

        var effectiveEnd = payload.End;
        var effectiveAllDay = payload.AllDay;

        // Đổi itemId nội bộ của Drive thành fileId/link để Google Calendar gắn attachment.
        IReadOnlyList<CalendarDriveAttachment>? driveAttachments = null;
        if (payload.DriveItemIds != null && payload.DriveItemIds.Count > 0)
        {
            var driveItems = await _items.GetByIdsAndUserAsync(payload.DriveItemIds, userId, ct);
            driveAttachments = driveItems
                .Where(item => item.ExternalId != null)
                .Select(item =>
                {
                    string? webViewLink = null;
                    string? mimeType = null;
                    if (!string.IsNullOrEmpty(item.MetadataJson))
                    {
                        try
                        {
                            var meta = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(item.MetadataJson);
                            if (meta != null)
                            {
                                if (meta.TryGetValue("webViewLink", out var wvl)) webViewLink = wvl.GetString();
                                if (meta.TryGetValue("mimeType", out var mt)) mimeType = mt.GetString();
                            }
                        }
                        catch { /* ignore parse errors */ }
                    }
                    return new CalendarDriveAttachment(item.ExternalId!, item.Title, mimeType, webViewLink);
                })
                .ToList();
        }

        var evDto = new CalendarEvent(
            "",
            null,
            payload.Title,
            payload.Description,
            payload.Start,
            effectiveEnd,
            payload.Location,
            payload.Attendees,
            effectiveAllDay,
            driveAttachments,
            null, // fullAttendees
            null, // meetUrl
            null, // htmlLink
            MapToGoogleReminders(payload.Reminders, effectiveAllDay),
            payload.Recurrence,
            OrganizerEmail: null,
            SelfResponseStatus: null,
            ICalUid: null,
            GuestsCanModify: payload.GuestsCanModify,
            GuestsCanInviteOthers: payload.GuestsCanInviteOthers,
            GuestsCanSeeOtherGuests: payload.GuestsCanSeeOtherGuests,
            SendUpdates: payload.SendUpdates
        );

        // Event được tạo sau khi FE đã xử lý quyền Drive nếu có guest + attachment.
        var created = await _calendarGateway.InsertEventAsync(conn, "primary", evDto, ct);

        var metaDict = new Dictionary<string, object>();
        if (created.Location != null) metaDict["location"] = created.Location;
        if (created.Attendees is { Count: > 0 }) metaDict["attendees"] = created.Attendees;
        if (created.Description != null) metaDict["description"] = created.Description;
        if (created.Recurrence != null && created.Recurrence.Count > 0) metaDict["recurrence"] = created.Recurrence;
        metaDict["organizerEmail"] = created.OrganizerEmail ?? conn.ProviderAccountId;
        metaDict["selfResponseStatus"] = created.SelfResponseStatus ?? "accepted";
        if (!string.IsNullOrWhiteSpace(created.ICalUid)) metaDict["iCalUid"] = created.ICalUid;
        metaDict["guestsCanModify"] = created.GuestsCanModify ?? payload.GuestsCanModify;
        metaDict["guestsCanInviteOthers"] = created.GuestsCanInviteOthers ?? payload.GuestsCanInviteOthers;
        metaDict["guestsCanSeeOtherGuests"] = created.GuestsCanSeeOtherGuests ?? payload.GuestsCanSeeOtherGuests;
        if (created.AllDay) metaDict["allDay"] = true;

        if (effectiveAllDay)
        {
            metaDict["start"] = payload.Start.ToString("yyyy-MM-dd");
            metaDict["end"] = effectiveEnd.ToString("yyyy-MM-dd");
        }
        else
        {
            metaDict["start"] = (created.Start?.UtcDateTime ?? payload.Start.UtcDateTime).ToString("o");
            metaDict["end"] = (created.End?.UtcDateTime ?? effectiveEnd.UtcDateTime).ToString("o");
        }

        if (payload.DriveItemIds != null && payload.DriveItemIds.Count > 0)
            metaDict["driveItemIds"] = payload.DriveItemIds;

        // Lưu thông tin Drive attachments vào metadata để FE hiển thị.
        if (driveAttachments != null && driveAttachments.Count > 0)
        {
            metaDict["driveAttachments"] = ToDriveAttachmentMetadata(driveAttachments);
        }

        var item = new Item
        {
            UserId = userId,
            ConnectionId = payload.ConnectionId,
            Type = ItemType.Event,
            ExternalId = created.Id,
            ETag = created.ETag,
            Title = created.Summary ?? "New Event",
            Snippet = created.Description ?? "",
            // All-day: dùng ngày theo offset gốc (khớp metadata["start"/"end"] = "yyyy-MM-dd"),
            // KHÔNG .UtcDateTime.Date (chuyển UTC trước làm lệch -1 ngày ở tz dương như +07:00).
            OccurredAt = effectiveAllDay
                ? DateTime.SpecifyKind(payload.Start.Date, DateTimeKind.Utc)
                : (created.Start?.UtcDateTime ?? DateTime.UtcNow),
            DueAt = effectiveAllDay
                ? DateTime.SpecifyKind(effectiveEnd.Date, DateTimeKind.Utc)
                : created.End?.UtcDateTime,
            MetadataJson = JsonSerializer.Serialize(metaDict)
        };

        if (payload.Reminders != null)
        {
            foreach (var r in payload.Reminders)
            {
                item.Reminders.Add(new EventReminder
                {
                    ReminderType = NormalizeReminderType(r.ReminderType),
                    OffsetValue = r.OffsetValue,
                    OffsetUnit = r.OffsetUnit,
                    TimeOfDay = r.TimeOfDay,
                    IsSent = false
                });
            }
        }

        await _items.AddAsync(item, ct);
        await _items.SaveChangesAsync(ct);
        if (_calendarInvitations != null)
            await _calendarInvitations.ReconcileOrganizerEventAsync(item, created, ct);
        return new ItemResponse(item.Id, item.Type, item.Title, item.Snippet, item.Status, item.OccurredAt, item.DueAt, item.IsImportant, item.ExternalId, item.MetadataJson, item.ItemFolders.Select(f => f.FolderId).ToList(), item.TagAssignments.Where(ta => ta.Tag != null).Select(ta => new ItemTag(ta.Tag.Id, ta.Tag.Name, ta.Tag.Color)).ToList(), item.ConnectionId);
    }


    public async Task<ItemResponse> CreateTicketAsync(Guid userId, CreateTicketRequest payload, CancellationToken ct = default)
    {
        var conn = await _connections.GetByIdAsync(payload.ConnectionId, ct);
        if (conn == null) throw new NotFoundException("Connection", payload.ConnectionId);
        if (conn.UserId != userId) throw new ForbiddenException("Not your connection.");
        if (conn.ServiceType != ServiceType.Jira) throw new BusinessRuleException("Connection is not for Jira.");
        if (conn.Status != ConnectionStatus.Active) throw new BusinessRuleException("Jira connection is not active. Please reconnect.");

        var createRequest = new CreateJiraIssueRequest(
            payload.ProjectKey,
            payload.IssueType,
            payload.Summary,
            payload.Description,
            payload.Assignee,
            payload.Priority,
            payload.Labels);

        // 1. Tạo issue trên Jira → lấy id + key.
        var created = await _jiraGateway.CreateIssueAsync(conn, createRequest, ct);

        // 2. Fetch lại issue đầy đủ field để map sang Item (status/priority/updated... do Jira quyết).
        var issue = await _jiraGateway.GetIssueAsync(conn, created.Key, ct);

        var item = _jiraMapper.ToItem(issue, userId, conn.Id);

        await _items.AddAsync(item, ct);
        await _items.SaveChangesAsync(ct);

        return new ItemResponse(item.Id, item.Type, item.Title, item.Snippet, item.Status, item.OccurredAt, item.DueAt, item.IsImportant, item.ExternalId, item.MetadataJson, item.ItemFolders.Select(f => f.FolderId).ToList(), item.TagAssignments.Where(ta => ta.Tag != null).Select(ta => new ItemTag(ta.Tag.Id, ta.Tag.Name, ta.Tag.Color)).ToList(), item.ConnectionId);
    }

    /// <summary>
    /// Write-back update cho Jira issue (SCRUM-57). Nội dung SỬA ĐƯỢC (khác Email immutable).
    /// Conflict: Jira không có HTTP ETag → dùng fields.updated làm version-token qua IWriteBackGuard.
    /// status đổi qua transition (không set field trực tiếp); comment là thao tác riêng.
    /// </summary>
    private async Task<ItemResponse> PatchTicketAsync(Item item, Connection conn, PatchItemRequest payload, CancellationToken ct)
    {
        if (conn.ServiceType != ServiceType.Jira) throw new BusinessRuleException("Connection type mismatch for Jira.");
        if (conn.Status != ConnectionStatus.Active) throw new BusinessRuleException("Jira connection is not active. Please reconnect.");
        if (item.ExternalId == null) throw new BusinessRuleException("Item has no external ID.");

        // Reject field của Google (Email/Event/File) gửi nhầm vào ticket.
        if (payload.IsUnread != null || payload.IsStarred != null || payload.AddLabels != null || payload.RemoveLabels != null ||
            payload.IsTrashed != null || payload.Title != null || payload.Start != null || payload.End != null ||
            payload.Location != null || payload.Attendees != null || payload.Name != null || payload.SendUpdates.HasValue)
        {
            throw new BusinessRuleException("Invalid fields for Jira ticket writeback.");
        }

        var key = item.ExternalId;

        // 1. Conflict check: lấy updated live từ provider, so với ETag đã lưu.
        var current = await _jiraGateway.GetIssueAsync(conn, key, ct);
        var providerEtag = current.Updated?.UtcDateTime.ToString("O");
        _guard.EnsureNoConflict(item.ETag, providerEtag);

        // 2. Update field (summary/description/priority/labels/issuetype) qua PUT /issue.
        if (payload.Summary != null || payload.Description != null || payload.Priority != null ||
            payload.Labels != null || payload.IssueType != null)
        {
            var updateReq = new UpdateJiraIssueRequest(
                payload.Summary,
                payload.Description,
                payload.Priority,
                payload.Labels,
                payload.IssueType);
            await _jiraGateway.UpdateIssueAsync(conn, key, updateReq, ct);
        }

        // 3. Reassign.
        if (payload.Assignee != null)
            await _jiraGateway.AssignIssueAsync(conn, key, payload.Assignee, ct);

        // 4. Đổi status qua transition (tra id từ tên/id; không khả dụng → 422).
        if (payload.StatusTransition != null)
        {
            var transitions = await _jiraGateway.GetTransitionsAsync(conn, key, ct);
            var match = transitions.FirstOrDefault(t =>
                string.Equals(t.Id, payload.StatusTransition, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Name, payload.StatusTransition, StringComparison.OrdinalIgnoreCase));
            if (match == null)
                throw new BusinessRuleException($"Transition '{payload.StatusTransition}' không khả dụng cho issue này theo workflow hiện tại.");
            await _jiraGateway.TransitionIssueAsync(conn, key, match.Id, ct);
        }

        // 5. Comment (thao tác riêng, không phải sửa field).
        if (!string.IsNullOrWhiteSpace(payload.Comment))
            await _jiraGateway.AddCommentAsync(conn, key, payload.Comment, ct: ct);

        // 6. Fetch lại để remap (Jira tự tính status/updated mới) + cập nhật ETag.
        var refreshed = await _jiraGateway.GetIssueAsync(conn, key, ct);
        var mapped = _jiraMapper.ToItem(refreshed, item.UserId, conn.Id);

        item.Title = mapped.Title;
        item.Snippet = mapped.Snippet;
        item.MetadataJson = mapped.MetadataJson;
        item.ETag = mapped.ETag;
        item.OccurredAt = mapped.OccurredAt;
        item.DueAt = mapped.DueAt;
        item.Status = mapped.Status;

        await _items.SaveChangesAsync(ct);
        return new ItemResponse(item.Id, item.Type, item.Title, item.Snippet, item.Status, item.OccurredAt, item.DueAt, item.IsImportant, item.ExternalId, item.MetadataJson, item.ItemFolders.Select(f => f.FolderId).ToList(), item.TagAssignments.Where(ta => ta.Tag != null).Select(ta => new ItemTag(ta.Tag.Id, ta.Tag.Name, ta.Tag.Color)).ToList(), item.ConnectionId);
    }

    public async Task DeleteItemAsync(Guid itemId, Guid userId, CancellationToken ct = default)
    {
        // Thiết kế: Xoá item không yêu cầu check ETag vì hành động xoá là dứt điểm, không quan tâm nội dung hiện tại
        var item = await _items.GetByIdAndUserAsync(itemId, userId, ct);
        if (item == null && _folders != null)
        {
            var isEditor = await _folders.IsItemSharedWithUserAsEditorAsync(itemId, userId, ct);
            if (isEditor)
            {
                item = await _items.GetByIdAsync(itemId, ct);
            }
        }
        if (item == null) await ThrowNoWriteAccessAsync(itemId, userId, ct);

        // Email gộp thread: mỗi thư trong hội thoại là 1 Item row riêng (do sync tách theo message).
        // Xoá "1 email" ở list = xoá CẢ thread — nếu chỉ trash/remove thư đại diện thì thread hiện lại
        // ở list với thư mới-nhì, và trên Gmail thread vẫn còn. Trash cả thread + xoá mọi row cùng ThreadId.
        // Trash cả thread bao gồm luôn trường hợp thread chỉ có 1 thư (kết quả giống trash 1 message).
        if (item.Type == ItemType.Email && item.ThreadId != null)
        {
            if (item.ConnectionId != null)
            {
                var emailConn = await _connections.GetByIdAsync(item.ConnectionId.Value, ct);
                if (emailConn != null)
                {
                    bool isTrashOrSpam = item.MetadataJson != null &&
                        (item.MetadataJson.Contains("\"TRASH\"") || item.MetadataJson.Contains("\"SPAM\""));

                    if (isTrashOrSpam)
                    {
                        try { await _gmailGateway.DeleteThreadAsync(emailConn, item.ThreadId, ct); }
                        catch (NotFoundException) { /* Đã xoá trên Gmail, tiếp tục xoá local */ }
                        catch (ForbiddenException) { /* Không đủ quyền xoá vĩnh viễn trên Gmail, chỉ xoá local */ }
                    }
                    else
                    {
                        // Provider lỗi bay lên trước khi xoá DB → Item local giữ nguyên (không lệch).
                        try { await _gmailGateway.TrashThreadAsync(emailConn, item.ThreadId, ct); }
                        catch (NotFoundException) { /* Đã xoá trên Gmail, tiếp tục xoá local */ }
                    }
                }
            }

            // Xoá local theo OWNER của item (item.UserId), không theo userId người thao tác:
            // shared-Editor xoá hộ thì userId là của người được share → filter không khớp row nào
            // → Gmail đã trash nhưng item local còn lại thành "email ma" (desync).
            await _items.DeleteThreadAsync(item.UserId, item.ThreadId, ct);
            return;
        }

        if (item.ExternalId != null && item.ConnectionId != null)
        {
            var conn = await _connections.GetByIdAsync(item.ConnectionId.Value, ct);
            if (conn != null)
            {
                switch (item.Type)
                {
                    case ItemType.Email:
                        bool isTrashOrSpamMsg = item.MetadataJson != null &&
                            (item.MetadataJson.Contains("\"TRASH\"") || item.MetadataJson.Contains("\"SPAM\""));

                        try
                        {
                            if (isTrashOrSpamMsg)
                            {
                                await _gmailGateway.DeleteMessageAsync(conn, item.ExternalId, ct);
                            }
                            else
                            {
                                await _gmailGateway.TrashMessageAsync(conn, item.ExternalId, ct);
                            }
                        }
                        catch (NotFoundException) { /* Đã xoá trên Gmail, tiếp tục xoá local */ }
                        catch (ForbiddenException) { /* Không đủ quyền xoá vĩnh viễn trên Gmail, chỉ xoá local */ }
                        break;
                    case ItemType.Event:
                        // Event đã bị xoá thẳng trên Google Calendar (hoặc nằm trên lịch mà connection
                        // này không sở hữu) → Google trả 404/410. Coi như đã xoá, dọn nốt row local
                        // thay vì ném ProviderError khiến item "ma" kẹt lại trong app.
                        try { await _calendarGateway.DeleteEventAsync(conn, "primary", item.ExternalId, ct); }
                        catch (NotFoundException) { /* Đã xoá trên Calendar, tiếp tục xoá local */ }
                        break;
                    case ItemType.File:
                        try { await _driveGateway.TrashFileAsync(conn, item.ExternalId, ct); }
                        catch (NotFoundException) { /* Đã xoá trên Drive, tiếp tục xoá local */ }
                        break;
                    case ItemType.Ticket:
                        // SCRUM-58: xoá issue trên Jira. Provider lỗi (403/502) bay lên trước khi Remove
                        // → Item local giữ nguyên (không xoá lệch).
                        await _jiraGateway.DeleteIssueAsync(conn, item.ExternalId, ct);
                        break;
                }
            }
        }

        // Event invitee copy có thể bị CalendarInvitation.InviteeItemId trỏ tới (FK NoAction).
        if (item.Type == ItemType.Event && _calendarInvitations != null)
            await _calendarInvitations.ClearInviteeItemLinksAsync(new[] { item.Id }, ct);

        _items.Remove(item);
        await _items.SaveChangesAsync(ct);
    }

    private static List<Dictionary<string, object?>> ToDriveAttachmentMetadata(IReadOnlyList<CalendarDriveAttachment> attachments)
        => attachments.Select(a => new Dictionary<string, object?>
        {
            ["fileId"] = a.FileId,
            ["title"] = a.Title,
            ["mimeType"] = a.MimeType,
            ["fileUrl"] = a.FileUrl,
        }).ToList();

    private static Dictionary<string, object> ParseMetadataDict(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new Dictionary<string, object>();
        return JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();
    }

    private static bool ReadMetaAllDay(Dictionary<string, object> meta)
    {
        if (!meta.TryGetValue("allDay", out var val)) return false;
        return val switch
        {
            JsonElement je => je.ValueKind == JsonValueKind.True,
            bool b => b,
            _ => false
        };
    }

    private static bool ReadMetaBool(Dictionary<string, object> meta, string key, bool fallback = false)
    {
        if (!meta.TryGetValue(key, out var value)) return fallback;
        return value switch
        {
            bool b => b,
            JsonElement element when element.ValueKind is JsonValueKind.True or JsonValueKind.False => element.GetBoolean(),
            _ => fallback
        };
    }

    private static string? ReadMetaString(Dictionary<string, object> meta, string key)
    {
        if (!meta.TryGetValue(key, out var val)) return null;
        return val switch
        {
            JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
            string s => s,
            _ => val?.ToString()
        };
    }

    private static DateTimeOffset? ReadMetaDateTime(Dictionary<string, object> meta, string key, DateTime? fallbackUtc)
    {
        var raw = ReadMetaString(meta, key);
        if (!string.IsNullOrEmpty(raw) && DateTimeOffset.TryParse(raw, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var dto))
            return dto;
        return fallbackUtc.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(fallbackUtc.Value, DateTimeKind.Utc)) : null;
    }

    private static DateTimeOffset ReadEventStart(Dictionary<string, object> meta, Item item)
        => ReadMetaDateTime(meta, "start", item.OccurredAt) ?? new DateTimeOffset(item.OccurredAt, TimeSpan.Zero);

    private static DateTimeOffset? ReadEventEnd(Dictionary<string, object> meta, Item item)
        => ReadMetaDateTime(meta, "end", item.DueAt) ?? (item.DueAt.HasValue ? new DateTimeOffset(item.DueAt.Value, TimeSpan.Zero) : null);

    private static List<CalendarEventReminder>? MapToGoogleReminders(IReadOnlyList<EventReminderDto>? localReminders, bool allDayStyle)
    {
        if (localReminders == null) return null;
        var list = new List<CalendarEventReminder>();
        foreach (var r in localReminders)
        {
            var minutes = GoogleCalendarReminderMapper.ToGoogleMinutes(r.OffsetUnit, r.OffsetValue, r.TimeOfDay, allDayStyle);

            var reminderType = NormalizeReminderType(r.ReminderType);
            if (reminderType == ReminderType.GooglePopup)
                list.Add(new CalendarEventReminder("popup", minutes));
            else if (reminderType == ReminderType.GoogleEmail)
                list.Add(new CalendarEventReminder("email", minutes));
        }
        return list;
    }

    private static ReminderType NormalizeReminderType(ReminderType reminderType)
        => Enum.IsDefined(reminderType) ? reminderType : ReminderType.GooglePopup;
}
