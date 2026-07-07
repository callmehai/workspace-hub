using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Mapping;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Services;

public class CalendarSyncService : ICalendarSyncService
{
    private readonly IGoogleCalendarGateway _gateway;
    private readonly ICalendarItemMapper _mapper;
    private readonly IItemRepository _items;
    private readonly IConnectionRepository _connections;

    public CalendarSyncService(
        IGoogleCalendarGateway gateway,
        ICalendarItemMapper mapper,
        IItemRepository items,
        IConnectionRepository connections)
    {
        _gateway = gateway;
        _mapper = mapper;
        _items = items;
        _connections = connections;
    }

    public async Task<SyncResult> SyncConnectionAsync(Connection connection, CancellationToken ct = default)
    {
        var existingItems = await _items.GetTrackedByConnectionIdAsync(connection.Id, ct);
        var newItems = new List<Item>();

        string? syncToken = connection.CursorType == CursorType.SyncToken ? connection.CursorValue : null;
        var result = await _gateway.SyncEventsAsync(connection, syncToken, ct);

        if (result.Expired)
            result = await _gateway.SyncEventsAsync(connection, null, ct);

        int scanned = result.Events.Count;
        int created = 0;
        int skipped = 0;

        foreach (var ev in result.Events)
        {
            var mapped = _mapper.ToItem(ev, connection.UserId, connection.Id);

            if (existingItems.TryGetValue(ev.Id, out var existing))
            {
                if (existing.ETag != mapped.ETag)
                {
                    existing.Title = mapped.Title;
                    existing.Snippet = mapped.Snippet;
                    existing.MetadataJson = mapped.MetadataJson;
                    existing.ETag = mapped.ETag;
                    existing.OccurredAt = mapped.OccurredAt;
                    existing.DueAt = mapped.DueAt;
                    // Keep Status intact to avoid resetting Kanban columns.
                }
                skipped++;
                continue;
            }

            newItems.Add(mapped);
            existingItems[ev.Id] = mapped;
            created++;
        }

        if (newItems.Count > 0)
            await _items.AddRangeAsync(newItems, ct);

        connection.CursorType = CursorType.SyncToken;
        connection.CursorValue = result.NextSyncToken;
        connection.LastSyncedAt = DateTime.UtcNow;
        connection.Status = ConnectionStatus.Active;
        connection.LastError = null;
        _connections.Update(connection);
        await _connections.SaveChangesAsync(ct);

        return new SyncResult(scanned, created, skipped, result.NextSyncToken);
    }
}
