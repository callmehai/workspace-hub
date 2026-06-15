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
        // a. Lấy historyId hiện tại
        var profile = await _gmailGateway.GetProfileAsync(connection, ct);
        var newCursor = profile.HistoryId?.ToString();

        // b. Lấy importantEmails
        var importantList = await _importantContacts.GetIdentifiersAsync(connection.UserId, ImportantContactType.Email, ct);
        var importantSet = new HashSet<string>(importantList, StringComparer.OrdinalIgnoreCase);

        // c. Lấy sẵn ExternalId đã có
        var existing = await _items.GetExistingExternalIdsAsync(connection.Id, ct);

        // d. Vòng lặp phân trang gom tối đa maxMessages id
        string? pageToken = null;
        var collectedIds = new List<string>();
        do
        {
            var page = await _gmailGateway.ListMessageIdsAsync(connection, pageToken, Math.Min(100, maxMessages - collectedIds.Count), ct);
            collectedIds.AddRange(page.MessageIds);
            pageToken = page.NextPageToken;
        } while (!string.IsNullOrEmpty(pageToken) && collectedIds.Count < maxMessages);

        // e. Duyệt các id
        var newItems = new List<Item>();
        int scanned = collectedIds.Count;
        int created = 0;
        int skipped = 0;

        foreach (var id in collectedIds)
        {
            if (existing.Contains(id))
            {
                skipped++;
                continue;
            }

            var msg = await _gmailGateway.GetMessageAsync(connection, id, ct);
            var item = _mapper.ToItem(msg, connection.UserId, connection.Id, importantSet);
            
            newItems.Add(item);
            existing.Add(id); // Tránh trùng lặp trong cùng 1 đợt
            created++;
        }

        // f. Lưu Items
        if (newItems.Any())
        {
            await _items.AddRangeAsync(newItems, ct);
            await _items.SaveChangesAsync(ct);
        }

        // g. Cập nhật connection
        connection.CursorType = CursorType.HistoryId;
        connection.CursorValue = newCursor;
        connection.LastSyncedAt = DateTime.UtcNow;
        connection.Status = ConnectionStatus.Active;
        connection.LastError = null;

        _connections.Update(connection);
        await _connections.SaveChangesAsync(ct);

        // h. Return
        return new SyncResult(scanned, created, skipped, newCursor);
    }
}
