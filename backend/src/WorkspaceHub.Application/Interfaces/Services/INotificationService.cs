using WorkspaceHub.Application.DTOs.Notifications;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Interfaces.Services;

public interface INotificationService
{
    Task<IReadOnlyList<NotificationDto>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);

    Task MarkAsReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default);

    Task MarkAllAsReadAsync(Guid userId, CancellationToken ct = default);

    Task<NotificationDto> CreateAndSendAsync(
        Guid userId,
        NotificationType type,
        string title,
        string body,
        string linkUrl,
        CancellationToken ct = default);

    /// <summary>
    /// Tạo notification cho nhiều user (bulk insert + publish từng người vì mỗi row có Id riêng).
    /// Dùng khi share folder, notify members, v.v.
    /// </summary>
    Task<IReadOnlyList<NotificationDto>> CreateAndSendToManyAsync(
        IReadOnlyList<Guid> userIds,
        NotificationType type,
        string title,
        string body,
        string linkUrl,
        CancellationToken ct = default);
}
