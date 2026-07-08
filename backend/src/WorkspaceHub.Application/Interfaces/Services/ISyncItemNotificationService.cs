namespace WorkspaceHub.Application.Interfaces.Services;

/// <summary>
/// Gửi notification + SignalR khi sync tạo item mới (mọi luồng: on-demand, cron, manual).
/// </summary>
public interface ISyncItemNotificationService
{
    Task NotifyNewItemsAsync(
        Guid connectionId,
        Guid userId,
        IReadOnlySet<string> beforeExternalIds,
        CancellationToken ct = default);
}
