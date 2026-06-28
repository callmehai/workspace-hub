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
    private readonly IWriteBackGuard _guard;
    private readonly IGmailGateway _gmailGateway;
    private readonly ICalendarGateway _calendarGateway;
    private readonly IDriveGateway _driveGateway;
    private readonly IJiraGateway _jiraGateway;
    private readonly IJiraItemMapper _jiraMapper;

    public ItemWriteBackService(
        IItemRepository items,
        IConnectionRepository connections,
        IWriteBackGuard guard,
        IGmailGateway gmailGateway,
        ICalendarGateway calendarGateway,
        IDriveGateway driveGateway,
        IJiraGateway jiraGateway,
        IJiraItemMapper jiraMapper)
    {
        _items = items;
        _connections = connections;
        _guard = guard;
        _gmailGateway = gmailGateway;
        _calendarGateway = calendarGateway;
        _driveGateway = driveGateway;
        _jiraGateway = jiraGateway;
        _jiraMapper = jiraMapper;
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
        if (item == null) throw new NotFoundException("Item", itemId);

        var conn = await GetConnectionAsync(item.ConnectionId, ct);

        if (item.Type == ItemType.Email && conn.ServiceType != ServiceType.Gmail) throw new BusinessRuleException("Connection type mismatch for Gmail.");
        if (item.Type == ItemType.Event && conn.ServiceType != ServiceType.GCal) throw new BusinessRuleException("Connection type mismatch for Calendar.");
        if (item.Type == ItemType.File && conn.ServiceType != ServiceType.Drive) throw new BusinessRuleException("Connection type mismatch for Drive.");

        if (item.ExternalId == null) throw new BusinessRuleException("Item has no external ID.");

        // belt-and-suspenders: validator already enforces this check, but keeping it for defense-in-depth.
        if (!payload.IsUnread.HasValue && !payload.IsStarred.HasValue && payload.AddLabels == null && payload.RemoveLabels == null &&
            !payload.IsTrashed.HasValue && payload.Title == null && payload.Start == null &&
            payload.End == null && payload.Location == null && payload.Attendees == null &&
            payload.Name == null)
        {
            throw new BusinessRuleException("No fields provided for update.");
        }

        string? providerEtag = null;
        switch (item.Type)
        {
            case ItemType.Email:
                providerEtag = await _gmailGateway.GetMessageETagAsync(conn, item.ExternalId, ct);
                break;
            case ItemType.Event:
                var ev = await _calendarGateway.GetEventAsync(conn, "primary", item.ExternalId, ct);
                providerEtag = ev.ETag;
                break;
            case ItemType.File:
                var file = await _driveGateway.GetFileAsync(conn, item.ExternalId, ct);
                providerEtag = file.ETag;
                break;
            default:
                throw new BusinessRuleException("Unsupported item type for writeback.");
        }

        _guard.EnsureNoConflict(item.ETag, providerEtag);

        string? newETag = null;

        switch (item.Type)
        {
            case ItemType.Email:
                if (payload.Title != null || payload.Start != null || payload.End != null || payload.Location != null || payload.Attendees != null || payload.Name != null)
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
                item.MetadataJson = JsonSerializer.Serialize(metaDictEmail);

                break;

            case ItemType.Event:
                if (payload.IsUnread != null || payload.IsStarred != null || payload.AddLabels != null || payload.RemoveLabels != null || payload.IsTrashed != null || payload.Name != null)
                    throw new BusinessRuleException("Invalid fields for Event writeback.");
                    
                var evDto = new CalendarEvent(
                    item.ExternalId,
                    providerEtag,
                    payload.Title,
                    null,
                    payload.Start,
                    payload.End,
                    payload.Location,
                    payload.Attendees
                );

                var updatedEvent = await _calendarGateway.UpdateEventAsync(conn, "primary", item.ExternalId, evDto, ct);
                newETag = updatedEvent.ETag;
                item.Title = updatedEvent.Summary ?? "No Title";
                item.OccurredAt = updatedEvent.Start?.UtcDateTime ?? DateTime.UtcNow;
                if (updatedEvent.End.HasValue) item.DueAt = updatedEvent.End.Value.UtcDateTime;

                var metaDictEvent = string.IsNullOrEmpty(item.MetadataJson) ? new Dictionary<string, object>() : JsonSerializer.Deserialize<Dictionary<string, object>>(item.MetadataJson) ?? new Dictionary<string, object>();
                if (updatedEvent.Location != null) metaDictEvent["location"] = updatedEvent.Location;
                if (updatedEvent.Attendees != null) metaDictEvent["attendees"] = updatedEvent.Attendees;
                item.MetadataJson = JsonSerializer.Serialize(metaDictEvent);
                break;

            case ItemType.File:
                if (payload.IsUnread != null || payload.IsStarred != null || payload.AddLabels != null || payload.RemoveLabels != null || payload.Title != null || payload.Start != null || payload.End != null || payload.Location != null || payload.Attendees != null)
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
        return new ItemResponse(item.Id, item.Type, item.Title, item.Snippet, item.Status, item.OccurredAt, item.DueAt, item.IsImportant, item.ExternalId, item.MetadataJson);
    }

    public async Task<ItemResponse> CreateEventAsync(Guid userId, CreateEventRequest payload, CancellationToken ct = default)
    {
        var conn = await _connections.GetByIdAsync(payload.ConnectionId, ct);
        if (conn == null) throw new NotFoundException("Connection", payload.ConnectionId);
        if (conn.UserId != userId) throw new ForbiddenException("Not your connection.");
        if (conn.ServiceType != ServiceType.GCal) throw new BusinessRuleException("Connection is not for Calendar.");

        var evDto = new CalendarEvent(
            "",
            null,
            payload.Title,
            null,
            payload.Start,
            payload.End,
            payload.Location,
            payload.Attendees
        );

        var created = await _calendarGateway.InsertEventAsync(conn, "primary", evDto, ct);

        var metaDict = new Dictionary<string, object>();
        if (created.Location != null) metaDict["location"] = created.Location;
        if (created.Attendees != null) metaDict["attendees"] = created.Attendees;

        var item = new Item
        {
            UserId = userId,
            ConnectionId = payload.ConnectionId,
            Type = ItemType.Event,
            ExternalId = created.Id,
            ETag = created.ETag,
            Title = created.Summary ?? "New Event",
            Snippet = created.Description ?? "",
            OccurredAt = created.Start?.UtcDateTime ?? DateTime.UtcNow,
            DueAt = created.End?.UtcDateTime,
            MetadataJson = JsonSerializer.Serialize(metaDict)
        };

        await _items.AddAsync(item, ct);
        await _items.SaveChangesAsync(ct);
        return new ItemResponse(item.Id, item.Type, item.Title, item.Snippet, item.Status, item.OccurredAt, item.DueAt, item.IsImportant, item.ExternalId, item.MetadataJson);
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

        return new ItemResponse(item.Id, item.Type, item.Title, item.Snippet, item.Status, item.OccurredAt, item.DueAt, item.IsImportant, item.ExternalId, item.MetadataJson);
    }

    public async Task DeleteItemAsync(Guid itemId, Guid userId, CancellationToken ct = default)
    {
        // Thiết kế: Xoá item không yêu cầu check ETag vì hành động xoá là dứt điểm, không quan tâm nội dung hiện tại
        var item = await _items.GetByIdAndUserAsync(itemId, userId, ct);
        if (item == null) throw new NotFoundException("Item", itemId);

        if (item.ExternalId != null && item.ConnectionId != null)
        {
            var conn = await _connections.GetByIdAsync(item.ConnectionId.Value, ct);
            if (conn != null)
            {
                switch (item.Type)
                {
                    case ItemType.Email:
                        await _gmailGateway.TrashMessageAsync(conn, item.ExternalId, ct);
                        break;
                    case ItemType.Event:
                        await _calendarGateway.DeleteEventAsync(conn, "primary", item.ExternalId, ct);
                        break;
                    case ItemType.File:
                        await _driveGateway.TrashFileAsync(conn, item.ExternalId, ct);
                        break;
                }
            }
        }

        _items.Remove(item);
        await _items.SaveChangesAsync(ct);
    }
}
