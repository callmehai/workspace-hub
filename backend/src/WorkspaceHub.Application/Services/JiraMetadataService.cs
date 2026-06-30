using Microsoft.Extensions.Caching.Memory;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Services;

public class JiraMetadataService : IJiraMetadataService
{
    private readonly IJiraGateway _gateway;
    private readonly IConnectionRepository _connections;
    private readonly IItemRepository _items;
    private readonly IMemoryCache _cache;

    // Cache nhẹ cho danh mục ít đổi (project/issue-type/priority). Transition + assignable-users KHÔNG cache.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public JiraMetadataService(
        IJiraGateway gateway,
        IConnectionRepository connections,
        IItemRepository items,
        IMemoryCache cache)
    {
        _gateway = gateway;
        _connections = connections;
        _items = items;
        _cache = cache;
    }

    public Task<IReadOnlyList<JiraProject>> GetProjectsAsync(Guid connectionId, Guid userId, CancellationToken ct = default) =>
        GetCachedAsync($"jira:projects:{connectionId}", connectionId, userId,
            (conn, c) => _gateway.GetProjectsAsync(conn, c), ct);

    public Task<IReadOnlyList<JiraIssueType>> GetIssueTypesAsync(Guid connectionId, Guid userId, string projectKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectKey))
            throw new BusinessRuleException("projectKey là bắt buộc.");

        return GetCachedAsync($"jira:issuetypes:{connectionId}:{projectKey}", connectionId, userId,
            (conn, c) => _gateway.GetIssueTypesAsync(conn, projectKey, c), ct);
    }

    public Task<IReadOnlyList<JiraPriority>> GetPrioritiesAsync(Guid connectionId, Guid userId, CancellationToken ct = default) =>
        GetCachedAsync($"jira:priorities:{connectionId}", connectionId, userId,
            (conn, c) => _gateway.GetPrioritiesAsync(conn, c), ct);

    public async Task<IReadOnlyList<JiraUser>> GetAssignableUsersAsync(Guid connectionId, Guid userId, string projectKey, string? query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectKey))
            throw new BusinessRuleException("projectKey là bắt buộc.");

        var conn = await GetValidJiraConnectionAsync(connectionId, userId, ct);
        return await _gateway.GetAssignableUsersAsync(conn, projectKey, query, ct);
    }

    public async Task<IReadOnlyList<JiraTransition>> GetTransitionsAsync(Guid connectionId, Guid userId, Guid itemId, CancellationToken ct = default)
    {
        var conn = await GetValidJiraConnectionAsync(connectionId, userId, ct);

        var item = await _items.GetByIdAndUserAsync(itemId, userId, ct)
            ?? throw new NotFoundException("Item", itemId);
        if (item.Type != ItemType.Ticket || string.IsNullOrEmpty(item.ExternalId))
            throw new BusinessRuleException("Item không phải Jira ticket.");
        if (item.ConnectionId != connectionId)
            throw new BusinessRuleException("Item không thuộc connection này.");

        return await _gateway.GetTransitionsAsync(conn, item.ExternalId, ct);
    }

    private async Task<IReadOnlyList<T>> GetCachedAsync<T>(
        string cacheKey,
        Guid connectionId,
        Guid userId,
        Func<Connection, CancellationToken, Task<IReadOnlyList<T>>> fetch,
        CancellationToken ct)
    {
        var conn = await GetValidJiraConnectionAsync(connectionId, userId, ct);

        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<T>? cached) && cached is not null)
            return cached;

        var result = await fetch(conn, ct);
        _cache.Set(cacheKey, result, CacheTtl);
        return result;
    }

    private async Task<Connection> GetValidJiraConnectionAsync(Guid connectionId, Guid userId, CancellationToken ct)
    {
        var conn = await _connections.GetByIdAsync(connectionId, ct);
        // Không lộ tồn tại connection của user khác → 404 cho cả "không có" lẫn "không phải của mình".
        if (conn is null || conn.UserId != userId)
            throw new NotFoundException("Connection", connectionId);
        if (conn.ServiceType != ServiceType.Jira)
            throw new BusinessRuleException("Kết nối này không phải Jira.");
        if (conn.Status != ConnectionStatus.Active)
            throw new BusinessRuleException("Jira connection không active. Vui lòng kết nối lại.");
        return conn;
    }
}
