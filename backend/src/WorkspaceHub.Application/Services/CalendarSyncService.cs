using System.Text.Json;
using Microsoft.Extensions.Logging;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Mapping;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Services;

public class CalendarSyncService : ICalendarSyncService
{
    private readonly ICalendarGateway _gateway;
    private readonly ICalendarItemMapper _mapper;
    private readonly IItemRepository _items;
    private readonly IConnectionRepository _connections;
    private readonly ICalendarInvitationService _calendarInvitations;
    private readonly ILogger<CalendarSyncService> _logger;

    public CalendarSyncService(
        ICalendarGateway gateway,
        ICalendarItemMapper mapper,
        IItemRepository items,
        IConnectionRepository connections,
        ICalendarInvitationService calendarInvitations,
        ILogger<CalendarSyncService> logger)
    {
        _gateway = gateway;
        _mapper = mapper;
        _items = items;
        _connections = connections;
        _calendarInvitations = calendarInvitations;
        _logger = logger;
    }

    public async Task<SyncResult> SyncConnectionAsync(Connection connection, CancellationToken ct = default)
    {
        var existingItems = await _items.GetTrackedByConnectionIdAsync(connection.Id, ct);
        var newItems = new List<Item>();
        var reconciliations = new List<(Item Item, CalendarEventDto Event)>();

        string? syncToken = connection.CursorType == CursorType.SyncToken ? connection.CursorValue : null;
        var result = await _gateway.SyncEventsAsync(connection, syncToken, ct);

        if (result.Expired)
            result = await _gateway.SyncEventsAsync(connection, null, ct);

        var driveFileIdToItemId = await BuildDriveFileLookupAsync(connection.UserId, result.Events, ct);

        int scanned = result.Events.Count;
        int created = 0;
        int skipped = 0;
        var anyUpdated = false;

        foreach (var ev in result.Events)
        {
            var mapped = _mapper.ToItem(ev, connection.UserId, connection.Id, driveFileIdToItemId);

            if (existingItems.TryGetValue(ev.Id, out var existing))
            {
                if (ShouldUpdateExisting(existing, mapped))
                {
                    existing.Title = mapped.Title;
                    existing.Snippet = mapped.Snippet;
                    existing.MetadataJson = mapped.MetadataJson;
                    existing.ETag = mapped.ETag;
                    existing.OccurredAt = mapped.OccurredAt;
                    existing.DueAt = mapped.DueAt;
                    SyncLocalReminders(existing, ev.Reminders, ev.AllDay);
                    anyUpdated = true;
                    // Keep Status intact to avoid resetting Kanban columns.
                }
                skipped++;
                reconciliations.Add((existing, ev));
                continue;
            }

            SyncLocalReminders(mapped, ev.Reminders, ev.AllDay);
            newItems.Add(mapped);
            existingItems[ev.Id] = mapped;
            reconciliations.Add((mapped, ev));
            created++;
        }

        if (newItems.Count > 0)
            await _items.AddRangeAsync(newItems, ct);

        if (newItems.Count > 0 || anyUpdated || result.CancelledEventIds.Count > 0)
            await _items.SaveChangesAsync(ct);

        // Incremental sync: event cancelled/xóa trên Google → xóa Item local.
        // Full sync (syncToken expired) không liệt kê mọi event cũ — không orphan-delete hàng loạt.
        // InviteeItemId = NoAction (SQL Server cấm SET NULL khi OrganizerItem đã CASCADE) → null hoá trước Remove.
        var cancelledItems = new List<Item>();
        foreach (var cancelledId in result.CancelledEventIds)
        {
            if (existingItems.Remove(cancelledId, out var toDelete))
                cancelledItems.Add(toDelete);
        }

        if (cancelledItems.Count > 0)
        {
            await _calendarInvitations.ClearInviteeItemLinksAsync(cancelledItems.Select(i => i.Id), ct);
            foreach (var toDelete in cancelledItems)
                _items.Remove(toDelete);
        }

        // Reconcile là best-effort: một event lỗi (vd RSVP tới event đã bị xoá trên Google)
        // không được chặn việc persist syncToken — nếu không, cursor không tiến và sync lặp lỗi mãi.
        foreach (var (localItem, calendarEvent) in reconciliations)
        {
            try
            {
                await _calendarInvitations.ReconcileSyncedEventAsync(connection, localItem, calendarEvent, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Reconcile invitation failed for item {ItemId} (event {ExternalId}); bỏ qua, cursor vẫn tiến.",
                    localItem.Id, localItem.ExternalId);
            }
        }

        connection.CursorType = CursorType.SyncToken;
        connection.CursorValue = result.NextSyncToken;
        connection.LastSyncedAt = DateTime.UtcNow;
        connection.Status = ConnectionStatus.Active;
        connection.LastError = null;
        _connections.Update(connection);
        await _connections.SaveChangesAsync(ct);

        return new SyncResult(scanned, created, skipped, result.NextSyncToken);
    }

    private async Task<IReadOnlyDictionary<string, Guid>> BuildDriveFileLookupAsync(
        Guid userId,
        IReadOnlyList<CalendarEventDto> events,
        CancellationToken ct)
    {
        var fileIds = events
            .SelectMany(e => e.DriveAttachments)
            .Select(a => a.FileId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (fileIds.Count == 0)
            return new Dictionary<string, Guid>();

        var driveItems = await _items.GetFilesByExternalIdsAsync(userId, fileIds, ct);
        return driveItems
            .Where(i => i.ExternalId != null)
            .ToDictionary(i => i.ExternalId!, i => i.Id, StringComparer.Ordinal);
    }

    /// <summary>
    /// Cập nhật khi ETag đổi, hoặc khi Google có attachment mà DB chưa có
    /// (event đã sync trước khi có code map attachment — ETag có thể đã khớp).
    /// </summary>
    private static bool ShouldUpdateExisting(Item existing, Item mapped)
    {
        if (existing.ETag != mapped.ETag)
            return true;

        if (!MetadataHasDriveAttachments(mapped.MetadataJson))
            return false;

        return !MetadataHasDriveAttachments(existing.MetadataJson);
    }

    private static bool MetadataHasDriveAttachments(string metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            return doc.RootElement.TryGetProperty("driveAttachments", out var arr)
                && arr.ValueKind == JsonValueKind.Array
                && arr.GetArrayLength() > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void SyncLocalReminders(Item item, List<CalendarEventReminder> gcalReminders, bool allDay)
    {
        var existingGoogleReminders = item.Reminders
            .Where(IsGoogleReminder)
            .ToList();

        foreach (var reminder in existingGoogleReminders)
        {
            item.Reminders.Remove(reminder);
        }

        var syncedGoogleReminders = gcalReminders
            .Select(r => new
            {
                ReminderType = MapGoogleReminderType(r.Method),
                Minutes = Math.Max(0, r.Minutes)
            })
            .Distinct()
            .ToList();

        foreach (var r in syncedGoogleReminders)
        {
            var (offsetValue, offsetUnit, timeOfDay) =
                GoogleCalendarReminderMapper.FromGoogleMinutes(r.Minutes, allDayStyle: allDay);
            item.Reminders.Add(new EventReminder
            {
                ReminderType = r.ReminderType,
                OffsetValue = offsetValue,
                OffsetUnit = offsetUnit,
                TimeOfDay = timeOfDay,
                IsSent = false
            });
        }
    }

    private static bool IsGoogleReminder(EventReminder reminder)
        => reminder.ReminderType is ReminderType.GooglePopup or ReminderType.GoogleEmail;

    private static ReminderType MapGoogleReminderType(string? method)
        => string.Equals(method, "email", StringComparison.OrdinalIgnoreCase)
            ? ReminderType.GoogleEmail
            : ReminderType.GooglePopup;
}
