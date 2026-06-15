using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Mapping;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Services;

public class GmailSyncService : IGmailSyncService
{
    private readonly IGmailGateway _gmailGateway;
    private readonly IGmailItemMapper _mapper;
    private readonly IItemRepository _items;
    private readonly IImportantContactRepository _importantContacts;
    private readonly IConnectionRepository _connections;

    public GmailSyncService(
        IGmailGateway gmailGateway,
        IGmailItemMapper mapper,
        IItemRepository items,
        IImportantContactRepository importantContacts,
        IConnectionRepository connections)
    {
        _gmailGateway = gmailGateway;
        _mapper = mapper;
        _items = items;
        _importantContacts = importantContacts;
        _connections = connections;
    }

    public async Task<SyncResult> SyncConnectionAsync(Connection connection, int maxMessages = 50, CancellationToken ct = default)
    {
        var importantList = await _importantContacts.GetIdentifiersAsync(connection.UserId, ImportantContactType.Email, ct);
        var importantSet = new HashSet<string>(importantList, StringComparer.OrdinalIgnoreCase);
        var existing = await _items.GetExistingExternalIdsAsync(connection.Id, ct);

        var newItems = new List<Item>();
        int scanned = 0;
        int created = 0;
        int skipped = 0;
        string? newCursor = null;

        if (string.IsNullOrEmpty(connection.CursorValue) || connection.CursorType != CursorType.HistoryId)
        {
            var fullResult = await FullSyncAsync(connection, maxMessages, ct);
            scanned = fullResult.CollectedIds.Count;
            newCursor = fullResult.NewCursor;

            var processResult = await ProcessMessageIdsAsync(connection, fullResult.CollectedIds, importantSet, existing, newItems, ct);
            created = processResult.Created;
            skipped = processResult.Skipped;
        }
        else
        {
            string? pageToken = null;
            string? latestHistoryId = null;
            bool expired = false;
            var addedIds = new List<string>();

            do
            {
                var h = await _gmailGateway.ListHistoryAsync(connection, connection.CursorValue, pageToken, ct);
                if (h.Expired)
                {
                    expired = true;
                    break;
                }
                
                addedIds.AddRange(h.AddedMessageIds);
                if (h.LatestHistoryId != null)
                {
                    latestHistoryId = h.LatestHistoryId;
                }
                pageToken = h.NextPageToken;

            } while (!string.IsNullOrEmpty(pageToken));

            if (expired)
            {
                var fullResult = await FullSyncAsync(connection, maxMessages, ct);
                scanned = fullResult.CollectedIds.Count;
                newCursor = fullResult.NewCursor;

                var processResult = await ProcessMessageIdsAsync(connection, fullResult.CollectedIds, importantSet, existing, newItems, ct);
                created = processResult.Created;
                skipped = processResult.Skipped;
            }
            else
            {
                scanned = addedIds.Count;
                newCursor = latestHistoryId ?? connection.CursorValue;

                var processResult = await ProcessMessageIdsAsync(connection, addedIds, importantSet, existing, newItems, ct);
                created = processResult.Created;
                skipped = processResult.Skipped;
            }
        }

        if (newItems.Any())
        {
            await _items.AddRangeAsync(newItems, ct);
            await _items.SaveChangesAsync(ct);
        }

        connection.CursorType = CursorType.HistoryId;
        connection.CursorValue = newCursor;
        connection.LastSyncedAt = DateTime.UtcNow;
        connection.Status = ConnectionStatus.Active;
        connection.LastError = null;

        _connections.Update(connection);
        await _connections.SaveChangesAsync(ct);

        return new SyncResult(scanned, created, skipped, newCursor);
    }

    private async Task<(int Created, int Skipped)> ProcessMessageIdsAsync(
        Connection connection, 
        IEnumerable<string> ids, 
        ISet<string> importantSet,
        HashSet<string> existing, 
        List<Item> newItems, 
        CancellationToken ct)
    {
        int created = 0;
        int skipped = 0;

        foreach (var id in ids)
        {
            if (existing.Contains(id))
            {
                skipped++;
                continue;
            }

            var msg = await _gmailGateway.GetMessageAsync(connection, id, ct);
            var item = _mapper.ToItem(msg, connection.UserId, connection.Id, importantSet);
            
            newItems.Add(item);
            existing.Add(id);
            created++;
        }

        return (created, skipped);
    }

    private async Task<(List<string> CollectedIds, string? NewCursor)> FullSyncAsync(Connection connection, int maxMessages, CancellationToken ct)
    {
        var profile = await _gmailGateway.GetProfileAsync(connection, ct);
        var newCursor = profile.HistoryId?.ToString();

        string? pageToken = null;
        var collectedIds = new List<string>();
        do
        {
            var page = await _gmailGateway.ListMessageIdsAsync(connection, pageToken, Math.Min(100, maxMessages - collectedIds.Count), ct);
            collectedIds.AddRange(page.MessageIds);
            pageToken = page.NextPageToken;
        } while (!string.IsNullOrEmpty(pageToken) && collectedIds.Count < maxMessages);

        return (collectedIds, newCursor);
    }
}
