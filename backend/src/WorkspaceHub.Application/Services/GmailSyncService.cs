using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    private readonly ITokenService _tokenService;
    private readonly ILogger<GmailSyncService> _logger;

    public GmailSyncService(
        IGmailGateway gmailGateway,
        IGmailItemMapper mapper,
        IItemRepository items,
        IImportantContactRepository importantContacts,
        IConnectionRepository connections,
        ITokenService tokenService,
        ILogger<GmailSyncService> logger)
    {
        _gmailGateway = gmailGateway;
        _mapper = mapper;
        _items = items;
        _importantContacts = importantContacts;
        _connections = connections;
        _tokenService = tokenService;
        _logger = logger;
    }

    private async Task<Connection> GetValidConnectionAsync(Guid connectionId, Guid userId, CancellationToken ct)
    {
        var conn = await _connections.GetByIdAsync(connectionId, ct);
        if (conn is null) throw new WorkspaceHub.Application.Common.NotFoundException("Connection", connectionId);
        if (conn.UserId != userId) throw new WorkspaceHub.Application.Common.NotFoundException("Connection", connectionId);

        if (conn.ServiceType != WorkspaceHub.Domain.Enums.ServiceType.Gmail)
            throw new WorkspaceHub.Application.Common.BusinessRuleException("Kết nối này không phải Gmail");

        return conn;
    }

    public async Task<GmailProfile> GetProfileAsync(Guid connectionId, Guid userId, CancellationToken ct = default)
    {
        var conn = await GetValidConnectionAsync(connectionId, userId, ct);
        return await _gmailGateway.GetProfileAsync(conn, ct);
    }

    public async Task<GmailSampleDto> GetSampleAsync(Guid connectionId, Guid userId, CancellationToken ct = default)
    {
        var conn = await GetValidConnectionAsync(connectionId, userId, ct);
        var list = await _gmailGateway.ListMessageIdsAsync(conn, null, 1, ct);
        if (list.MessageIds.Count == 0)
        {
            throw new WorkspaceHub.Application.Common.NotFoundException("Hộp thư trống, không có email để map");
        }

        var msg = await _gmailGateway.GetMessageAsync(conn, list.MessageIds[0], ct);
        var item = _mapper.ToItem(msg, conn.UserId, conn.Id, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        return new GmailSampleDto(
            item.ExternalId,
            item.Type.ToString(),
            item.Title,
            item.Snippet,
            item.IsImportant,
            item.OccurredAt,
            item.MetadataJson);
    }

    public async Task<SyncResult> SyncConnectionAsync(Connection connection, int maxMessages = 50, CancellationToken ct = default)
    {
        var importantList = await _importantContacts.GetIdentifiersAsync(connection.UserId, ImportantContactType.Email, ct);
        var importantSet = new HashSet<string>(importantList, StringComparer.OrdinalIgnoreCase);
        var existingItems = await _items.GetTrackedByConnectionIdAsync(connection.Id, ct);

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

            var processResult = await ProcessMessageIdsAsync(connection, fullResult.CollectedIds, importantSet, existingItems, newItems, ct);
            created = processResult.Created;
            skipped = processResult.Skipped;
        }
        else
        {
            string? pageToken = null;
            string? latestHistoryId = null;
            bool expired = false;
            var affectedIds = new List<string>();

            do
            {
                var h = await _gmailGateway.ListHistoryAsync(connection, connection.CursorValue, pageToken, ct);
                if (h.Expired)
                {
                    expired = true;
                    break;
                }

                affectedIds.AddRange(h.AffectedMessageIds);
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

                var processResult = await ProcessMessageIdsAsync(connection, fullResult.CollectedIds, importantSet, existingItems, newItems, ct);
                created = processResult.Created;
                skipped = processResult.Skipped;
            }
            else
            {
                scanned = affectedIds.Count;
                newCursor = latestHistoryId ?? connection.CursorValue;

                var processResult = await ProcessMessageIdsAsync(connection, affectedIds, importantSet, existingItems, newItems, ct);
                created = processResult.Created;
                skipped = processResult.Skipped;
            }
        }

        if (newItems.Count > 0)
        {
            await _items.AddRangeAsync(newItems, ct);
            try
            {
                await _items.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Xoá tất cả khỏi ChangeTracker trước khi thử lại
                foreach (var item in newItems)
                    _items.Remove(item);

                // Lưu từng item một để không làm rollback toàn bộ batch
                foreach (var item in newItems)
                {
                    await _items.AddAsync(item, ct);
                    try
                    {
                        await _items.SaveChangesAsync(ct);
                    }
                    catch (DbUpdateException)
                    {
                        _items.Remove(item);
                        skipped++;
                        created--;
                    }
                }
            }
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
        Dictionary<string, Item> existingItems,
        List<Item> newItems,
        CancellationToken ct)
    {
        var idsToFetch = ids.Distinct().ToList();
        if (idsToFetch.Count == 0) return (0, 0);

        var semaphore = new SemaphoreSlim(10); // Concurrent limit 10
        try
        {
            // Pre-refresh token once before fanning out to avoid DB concurrency issues
            await _tokenService.GetFreshAccessTokenAsync(connection, ct);

            var fetchTasks = idsToFetch.Select(async id =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    var msg = await _gmailGateway.GetMessageAsync(connection, id, ct);
                    return _mapper.ToItem(msg, connection.UserId, connection.Id, importantSet);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch Gmail message {MessageId} for connection {ConnectionId}. Skipping.", id, connection.Id);
                    return null;
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var results = await Task.WhenAll(fetchTasks);
            
            var successfullyCreatedCount = 0;
            var skippedCount = 0;
            for (int i = 0; i < results.Length; i++)
            {
                var mapped = results[i];
                if (mapped != null)
                {
                    if (existingItems.TryGetValue(mapped.ExternalId!, out var existing))
                    {
                        // Check if ETag (HistoryId) differs. If so, update fields.
                        if (existing.ETag != mapped.ETag)
                        {
                            existing.Title = mapped.Title;
                            existing.Snippet = mapped.Snippet;
                            existing.MetadataJson = mapped.MetadataJson;
                            existing.ETag = mapped.ETag;
                            existing.OccurredAt = mapped.OccurredAt;
                            existing.IsImportant = mapped.IsImportant;
                            // Keep existing.Status intact to avoid overwriting Kanban columns.
                        }
                        skippedCount++;
                    }
                    else
                    {
                        newItems.Add(mapped);
                        existingItems[mapped.ExternalId!] = mapped;
                        successfullyCreatedCount++;
                    }
                }
            }

            return (successfullyCreatedCount, skippedCount);
        }
        finally
        {
            semaphore.Dispose();
        }
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
