namespace WorkspaceHub.Application.DTOs.Admin;

/// <summary>
/// Thống kê hệ thống cho admin dashboard.
/// syncErrorsLast24h = số Connection có Status=Error VÀ LastSyncedAt >= UtcNow-24h (null = excluded).
/// connectionsByStatus = groupby enum string (chỉ key có count > 0 xuất hiện).
/// activeUsers + lockedUsers phải bằng totalUsers.
/// </summary>
public record AdminStatsDto(
    int TotalUsers,
    int ActiveUsers,
    int LockedUsers,
    int TotalConnections,
    Dictionary<string, int> ConnectionsByStatus,
    int TotalItems,
    int SyncErrorsLast24h);
