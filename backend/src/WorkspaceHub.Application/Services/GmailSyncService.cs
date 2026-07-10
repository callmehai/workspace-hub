using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;
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
    private readonly IPeopleGateway _peopleGateway;
    private readonly IGoogleContactRepository _googleContacts;
    private readonly IGoogleContactMapper _googleContactMapper;
    private readonly ILogger<GmailSyncService> _logger;

    public GmailSyncService(
        IGmailGateway gmailGateway,
        IGmailItemMapper mapper,
        IItemRepository items,
        IImportantContactRepository importantContacts,
        IConnectionRepository connections,
        ITokenService tokenService,
        IPeopleGateway peopleGateway,
        IGoogleContactRepository googleContacts,
        IGoogleContactMapper googleContactMapper,
        ILogger<GmailSyncService> logger)
    {
        _gmailGateway = gmailGateway;
        _mapper = mapper;
        _items = items;
        _importantContacts = importantContacts;
        _connections = connections;
        _tokenService = tokenService;
        _peopleGateway = peopleGateway;
        _googleContacts = googleContacts;
        _googleContactMapper = googleContactMapper;
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
        var list = await _gmailGateway.ListMessageIdsAsync(conn, null, 1, ct: ct);
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

        // discoveryIds: từ listing (global recent + các hộp thư) — chỉ fetch cái CHƯA có (khám phá thư mới).
        var discoveryIds = new HashSet<string>(StringComparer.Ordinal);
        // changedIds: từ incremental history — luôn re-fetch để cập nhật (đổi label/đã đọc/ETag).
        var changedIds = new List<string>();

        bool needFull = string.IsNullOrEmpty(connection.CursorValue) || connection.CursorType != CursorType.HistoryId;

        if (!needFull)
        {
            string? pageToken = null;
            string? latestHistoryId = null;
            bool expired = false;

            do
            {
                var h = await _gmailGateway.ListHistoryAsync(connection, connection.CursorValue!, pageToken, ct);
                if (h.Expired) { expired = true; break; }
                changedIds.AddRange(h.AffectedMessageIds);
                if (h.LatestHistoryId != null) latestHistoryId = h.LatestHistoryId;
                pageToken = h.NextPageToken;
            } while (!string.IsNullOrEmpty(pageToken));

            if (expired) needFull = true;
            else newCursor = latestHistoryId ?? connection.CursorValue;
        }

        if (needFull)
        {
            var full = await FullSyncAsync(connection, maxMessages, ct);
            discoveryIds.UnionWith(full.CollectedIds);
            newCursor = full.NewCursor;
        }

        // LUÔN quét recent từng hộp thư (SENT/DRAFT/STARRED/CATEGORY_*/SPAM/TRASH) — kể cả sync incremental.
        // Gmail history KHÔNG báo tin spam/trash mới → nếu chỉ dựa history thì spam/trash chỉ vào khi user
        // tương tác. Quét chủ động ở đây để chúng xuất hiện tự động; chỉ fetch id CHƯA có nên vẫn nhẹ.
        await CollectMailboxesAsync(connection, discoveryIds, ct);

        var idsToProcess = changedIds
            .Concat(discoveryIds.Where(id => !existingItems.ContainsKey(id)))
            .Distinct()
            .ToList();
        scanned = idsToProcess.Count;

        var processResult = await ProcessMessageIdsAsync(connection, idsToProcess, importantSet, existingItems, newItems, ct);
        created = processResult.Created;
        skipped = processResult.Skipped;

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

        await SyncContactsAsync(connection, ct);

        return new SyncResult(scanned, created, skipped, newCursor);
    }

/// <summary>Đồng bộ contact Google vào cache DB (best-effort — lỗi không làm fail mail sync). Chạy cuối mỗi lần sync Gmail (cron định kỳ, manual, hoặc lazy).</summary>
    private async Task SyncContactsAsync(Connection connection, CancellationToken ct)
    {
        try
        {
            var rows = await _peopleGateway.ListAllAsync(connection, ct);
            var syncedAt = DateTime.UtcNow;
            var entities = rows
                .Select(r => _googleContactMapper.ToEntity(r, connection.Id, syncedAt))
                .ToList();

            await _googleContacts.SyncForConnectionAsync(connection.Id, entities, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Contact sync skipped for connection {ConnectionId}", connection.Id);
        }
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
                            existing.MetadataJson = MergeMetadata(existing.MetadataJson, mapped.MetadataJson);
                            existing.ETag = mapped.ETag;
                            existing.OccurredAt = mapped.OccurredAt;
                            existing.IsImportant = mapped.IsImportant;
                            // Keep existing.Status intact to avoid overwriting Kanban columns.
                        }
                        else
                        {
                            // Even if ETag is the same, merge metadata to ensure draft details are preserved
                            existing.MetadataJson = MergeMetadata(existing.MetadataJson, mapped.MetadataJson);
                        }
                        skippedCount++;
                    }
                    else
                    {
                        // For DRAFT messages: Gmail changes messageId on each draft update.
                        // Check if we already have a local item with the same draftId OR same threadId+DRAFT label
                        // → update ExternalId instead of creating a duplicate (which would lose the user's bodyHtml/subject).
                        bool mappedIsDraft = IsDraftItem(mapped.MetadataJson);
                        Item? existingDraft = null;

                        if (mappedIsDraft)
                        {
                            var mappedDraftId = ExtractDraftIdFromMetadata(mapped.MetadataJson);

                            // Strategy 1: match by draftId (most reliable when available)
                            if (mappedDraftId != null)
                            {
                                existingDraft = existingItems.Values.FirstOrDefault(item =>
                                {
                                    var id = ExtractDraftIdFromMetadata(item.MetadataJson);
                                    return id != null && id == mappedDraftId;
                                });
                            }

                            // Strategy 2: match by threadId + DRAFT label (Gmail allows max 1 draft per thread)
                            if (existingDraft == null && !string.IsNullOrEmpty(mapped.ThreadId))
                            {
                                existingDraft = existingItems.Values.FirstOrDefault(item =>
                                    item.ThreadId == mapped.ThreadId
                                    && item.ExternalId != mapped.ExternalId
                                    && IsDraftItem(item.MetadataJson));
                            }
                        }

                        if (existingDraft != null)
                        {
                            // Update the existing draft item's ExternalId to the new message ID
                            existingItems.Remove(existingDraft.ExternalId!);
                            existingDraft.ExternalId = mapped.ExternalId;
                            existingDraft.ThreadId = mapped.ThreadId;
                            existingDraft.ETag = mapped.ETag;
                            existingDraft.OccurredAt = mapped.OccurredAt;
                            existingDraft.MetadataJson = MergeMetadata(existingDraft.MetadataJson, mapped.MetadataJson);
                            existingItems[mapped.ExternalId!] = existingDraft;
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
            }

            return (successfullyCreatedCount, skippedCount);
        }
        finally
        {
            semaphore.Dispose();
        }
    }

    // Hộp thư kéo theo LABEL (messages.list labelIds) — recent mỗi hộp để mailbox nào cũng có dữ liệu.
    private static readonly string[] MailboxLabels =
        { "SENT", "DRAFT", "STARRED", "CATEGORY_PROMOTIONS", "CATEGORY_SOCIAL", "CATEGORY_UPDATES" };
    // Hộp thư kéo theo QUERY (q=in:spam/in:trash). labelIds+includeSpamTrash KHÔNG kéo được Spam/Trash
    // ổn định (thực nghiệm: trả 0), dùng toán tử `in:` mới đáng tin.
    private static readonly string[] MailboxQueries = { "in:spam", "in:trash" };
    private const int PerLabelBatch = 50;

    /// <summary>Full sync = chỉ quét recent TOÀN hộp thư (INBOX + thư mới). Mailbox phụ do CollectMailboxesAsync lo.</summary>
    private async Task<(List<string> CollectedIds, string? NewCursor)> FullSyncAsync(Connection connection, int maxMessages, CancellationToken ct)
    {
        var profile = await _gmailGateway.GetProfileAsync(connection, ct);
        var newCursor = profile.HistoryId?.ToString();

        var ids = await CollectListAsync(connection, null, null, maxMessages, ct);
        return (ids, newCursor);
    }

    /// <summary>Quét recent MỌI hộp thư phụ (label + query) SONG SONG, gộp vào set (dedupe). Chạy mỗi lần sync.</summary>
    private async Task CollectMailboxesAsync(Connection connection, HashSet<string> into, CancellationToken ct)
    {
        var tasks = new List<Task<List<string>>>();
        foreach (var label in MailboxLabels)
            tasks.Add(CollectListAsync(connection, new[] { label }, null, PerLabelBatch, ct));
        foreach (var q in MailboxQueries)
            tasks.Add(CollectListAsync(connection, null, q, PerLabelBatch, ct));

        var results = await Task.WhenAll(tasks);
        foreach (var list in results)
            foreach (var id in list) into.Add(id);
    }

    /// <summary>List recent message-id theo label HOẶC query (q), tối đa <paramref name="max"/>. Trả list (chạy song song được).</summary>
    private async Task<List<string>> CollectListAsync(Connection connection, IReadOnlyList<string>? labelIds, string? query, int max, CancellationToken ct)
    {
        var result = new List<string>();
        string? pageToken = null;
        var pulled = 0;
        do
        {
            var take = Math.Min(100, max - pulled);
            if (take <= 0) break;
            var page = await _gmailGateway.ListMessageIdsAsync(connection, pageToken, take, labelIds, query, ct);
            foreach (var id in page.MessageIds) { result.Add(id); pulled++; }
            pageToken = page.NextPageToken;
        } while (!string.IsNullOrEmpty(pageToken) && pulled < max);
        return result;
    }

    private static string MergeMetadata(string? existingJson, string newJson)
    {
        if (string.IsNullOrEmpty(existingJson)) return newJson;
        try
        {
            var existingDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(existingJson);
            var newDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(newJson);
            if (existingDict == null || newDict == null) return newJson;

            var mergedDict = new Dictionary<string, object>();
            foreach (var kv in newDict)
            {
                mergedDict[kv.Key] = kv.Value;
            }

            // draftId + rfc822MessageId là ĐỊNH DANH ổn định — bản sync theo message-list thường không kèm.
            // Giữ lại từ metadata cũ CHỈ KHI bản mới thiếu, và không bao giờ ghi đè giá trị mới nếu Gmail có trả về.
            var identityKeys = new[] { "draftId", "rfc822MessageId" };
            foreach (var key in identityKeys)
            {
                bool newHasValue = newDict.TryGetValue(key, out var nv)
                    && nv.ValueKind != JsonValueKind.Null && nv.ValueKind != JsonValueKind.Undefined
                    && !(nv.ValueKind == JsonValueKind.String && string.IsNullOrEmpty(nv.GetString()));
                if (!newHasValue
                    && existingDict.TryGetValue(key, out var val)
                    && val.ValueKind != JsonValueKind.Null && val.ValueKind != JsonValueKind.Undefined)
                {
                    mergedDict[key] = val;
                }
            }

            // subject + bodyHtml là NỘI DUNG có thể user sửa trực tiếp trên Gmail (không qua app).
            // Chỉ preserve giá trị cũ khi bản sync mới THIẾU/RỖNG — nếu không sẽ "đóng băng" nội dung cũ vĩnh viễn.
            var contentStringKeys = new[] { "subject", "bodyHtml" };
            foreach (var key in contentStringKeys)
            {
                bool newHasValue = newDict.TryGetValue(key, out var nv)
                    && nv.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(nv.GetString());
                if (!newHasValue
                    && existingDict.TryGetValue(key, out var val)
                    && val.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(val.GetString()))
                {
                    mergedDict[key] = val;
                }
            }

            // to, cc, bcc: chỉ preserve khi bản mới thiếu/rỗng (giữ nguyên hành vi cũ).
            var recipientKeys = new[] { "to", "cc", "bcc" };
            foreach (var key in recipientKeys)
            {
                if (existingDict.TryGetValue(key, out var existingVal) && existingVal.ValueKind == JsonValueKind.Array && existingVal.GetArrayLength() > 0)
                {
                    if (!newDict.TryGetValue(key, out var newVal) || newVal.ValueKind != JsonValueKind.Array || newVal.GetArrayLength() == 0)
                    {
                        mergedDict[key] = existingVal;
                    }
                }
            }

            return JsonSerializer.Serialize(mergedDict);
        }
        catch
        {
            return newJson;
        }
    }

    /// <summary>Extract the Gmail "draftId" string from an item's MetadataJson (if present).</summary>
    private static string? ExtractDraftIdFromMetadata(string? metadataJson)
    {
        if (string.IsNullOrEmpty(metadataJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            if (doc.RootElement.TryGetProperty("draftId", out var val) && val.ValueKind == JsonValueKind.String)
            {
                return val.GetString();
            }
        }
        catch { /* ignore */ }
        return null;
    }

    /// <summary>Check if an item's MetadataJson indicates it is a DRAFT email.</summary>
    private static bool IsDraftItem(string? metadataJson)
    {
        if (string.IsNullOrEmpty(metadataJson)) return false;
        // Quick string check before parsing JSON
        return metadataJson.Contains("\"DRAFT\"");
    }
}
