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
        var existing = await _items.GetExistingExternalIdsAsync(connection.Id, ct);
        var newItems = new List<Item>();

        string? syncToken = connection.CursorType == CursorType.SyncToken ? connection.CursorValue : null;
        var result = await _gateway.SyncEventsAsync(connection, syncToken, ct);
        if (result.Expired)
        {
            result = await _gateway.SyncEventsAsync(connection, null, ct);
        }

        int scanned = result.Events.Count;
        int created = 0;
        int skipped = 0;

        foreach (var ev in result.Events)
        {
            if (existing.Contains(ev.Id))
            {
                skipped++;
                continue;
            }
            var item = _mapper.ToItem(ev, connection.UserId, connection.Id);
            newItems.Add(item);
            existing.Add(ev.Id);
            created++;
        }

        if (newItems.Any())
        {
            await _items.AddRangeAsync(newItems, ct);
            await _items.SaveChangesAsync(ct);
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
}
