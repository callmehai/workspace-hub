using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Mapping;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Services;

/// <summary>
/// Đọc/sync issue Jira về Item(Type=Ticket). Tái dùng kiến trúc on-demand của Google (SCRUM-16):
/// KHÔNG pull định kỳ, KHÔNG webhook. Cursor JqlUpdated lưu mốc fields.updated gần nhất.
/// </summary>
public class JiraSyncService : IJiraSyncService
{
    private readonly IJiraGateway _gateway;
    private readonly IJiraItemMapper _mapper;
    private readonly IItemRepository _items;
    private readonly IConnectionRepository _connections;

    private const int PageSize = 100;

    // Jira filter "updated >= ..." chỉ chính xác tới PHÚT → lùi cursor 1 phút khi build JQL
    // để không bỏ sót issue đổi trong cùng phút mốc trước (dedupe lo phần kéo trùng).
    private static readonly TimeSpan CursorSlack = TimeSpan.FromMinutes(1);

    // Jira yêu cầu định dạng datetime trong JQL là "yyyy/MM/dd HH:mm".
    private const string JqlDateFormat = "yyyy/MM/dd HH:mm";

    public JiraSyncService(
        IJiraGateway gateway,
        IJiraItemMapper mapper,
        IItemRepository items,
        IConnectionRepository connections)
    {
        _gateway = gateway;
        _mapper = mapper;
        _items = items;
        _connections = connections;
    }

    public async Task<SyncResult> SyncConnectionAsync(Connection connection, int maxIssues = 50, CancellationToken ct = default)
    {
        if (connection.ServiceType != ServiceType.Jira)
            throw new BusinessRuleException("Kết nối này không phải Jira");

        // Tracked existing items keyed theo ExternalId → cập nhật được item đã sync (issue đổi title/status...).
        var existingItems = await _items.GetTrackedByConnectionIdAsync(connection.Id, ct);
        var jql = BuildJql(connection);

        var newItems = new List<Item>();
        int scanned = 0;
        int created = 0;
        int skipped = 0;
        DateTimeOffset? maxUpdated = ParseCursor(connection.CursorValue);

        string? pageToken = null;
        do
        {
            var page = await _gateway.SearchIssuesAsync(
                connection, jql, pageToken, Math.Min(PageSize, maxIssues - scanned), ct);

            foreach (var issue in page.Issues)
            {
                scanned++;

                if (issue.Updated.HasValue && (maxUpdated is null || issue.Updated > maxUpdated))
                    maxUpdated = issue.Updated;

                var mapped = _mapper.ToItem(issue, connection.UserId, connection.Id);

                if (existingItems.TryGetValue(issue.Id, out var existing))
                {
                    // Issue đã sync nhưng có thể đã đổi → cập nhật field (giữ nguyên Id/Status Kanban/folders local).
                    ApplyProviderFields(existing, mapped);
                    skipped++;
                    continue;
                }

                newItems.Add(mapped);
                existingItems[issue.Id] = mapped;
                created++;
            }

            pageToken = page.IsLast ? null : page.NextPageToken;

        } while (!string.IsNullOrEmpty(pageToken) && scanned < maxIssues);

        (created, skipped) = await PersistAsync(newItems, created, skipped, ct);

        connection.CursorType = CursorType.JqlUpdated;
        if (maxUpdated.HasValue)
            connection.CursorValue = maxUpdated.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        connection.LastSyncedAt = DateTime.UtcNow;
        connection.Status = ConnectionStatus.Active;
        connection.LastError = null;

        _connections.Update(connection);
        await _connections.SaveChangesAsync(ct);

        return new SyncResult(scanned, created, skipped, connection.CursorValue);
    }

    /// <summary>
    /// Cập nhật field do provider quyết lên item đã tồn tại (re-sync issue đổi).
    /// GIỮ NGUYÊN field local: Id, Status (cột Kanban user kéo), IsArchived, folders/tags.
    /// </summary>
    private static void ApplyProviderFields(Item existing, Item mapped)
    {
        existing.Title = mapped.Title;
        existing.Snippet = mapped.Snippet;
        existing.MetadataJson = mapped.MetadataJson;
        existing.ETag = mapped.ETag;
        existing.OccurredAt = mapped.OccurredAt;
        existing.IsImportant = mapped.IsImportant;
    }

    /// <summary>Lưu items; nếu vi phạm UNIQUE(ConnectionId, ExternalId) thì lưu lại từng cái, không hỏng cả batch.</summary>
    private async Task<(int Created, int Skipped)> PersistAsync(List<Item> newItems, int created, int skipped, CancellationToken ct)
    {
        if (newItems.Count == 0)
            return (created, skipped);

        await _items.AddRangeAsync(newItems, ct);
        try
        {
            await _items.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            foreach (var item in newItems)
                _items.Remove(item);

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

        return (created, skipped);
    }

    private static string BuildJql(Connection connection)
    {
        const string baseJql = "(assignee = currentUser() OR reporter = currentUser())";

        var cursor = ParseCursor(connection.CursorValue);
        if (connection.CursorType == CursorType.JqlUpdated && cursor.HasValue)
        {
            var since = cursor.Value.UtcDateTime.Subtract(CursorSlack).ToString(JqlDateFormat, CultureInfo.InvariantCulture);
            return $"{baseJql} AND updated >= \"{since}\" ORDER BY updated ASC";
        }

        return $"{baseJql} ORDER BY updated ASC";
    }

    private static DateTimeOffset? ParseCursor(string? cursorValue)
    {
        if (string.IsNullOrWhiteSpace(cursorValue))
            return null;

        return DateTimeOffset.TryParse(
            cursorValue, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
