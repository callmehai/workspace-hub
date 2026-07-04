using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs.Notifications;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Services;

public class NotificationService : INotificationService
{
    private readonly INotificationRepository _notifications;
    private readonly INotificationPublisher _publisher;

    public NotificationService(INotificationRepository notifications, INotificationPublisher publisher)
    {
        _notifications = notifications;
        _publisher = publisher;
    }

    public async Task<IReadOnlyList<NotificationDto>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        var items = await _notifications.GetByUserIdAsync(userId, ct);
        return items.Select(MapToDto).ToList().AsReadOnly();
    }

    public async Task MarkAsReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default)
    {
        var notification = await _notifications.GetByIdForUserAsync(userId, notificationId, ct)
            ?? throw new NotFoundException(nameof(Notification), notificationId);

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            _notifications.Update(notification);
            await _notifications.SaveChangesAsync(ct);
        }
    }

    public Task MarkAllAsReadAsync(Guid userId, CancellationToken ct = default)
        => _notifications.MarkAllAsReadAsync(userId, ct);

    public async Task<NotificationDto> CreateAndSendAsync(
        Guid userId,
        NotificationType type,
        string title,
        string body,
        string linkUrl,
        CancellationToken ct = default)
    {
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = type,
            Title = title,
            Body = body,
            LinkUrl = linkUrl,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        await _notifications.AddAsync(notification, ct);
        await _notifications.SaveChangesAsync(ct);

        var dto = MapToDto(notification);
        await _publisher.PublishToUserAsync(userId, dto, ct);
        return dto;
    }

    public async Task<IReadOnlyList<NotificationDto>> CreateAndSendToManyAsync(
        IReadOnlyList<Guid> userIds,
        NotificationType type,
        string title,
        string body,
        string linkUrl,
        CancellationToken ct = default)
    {
        if (userIds.Count == 0)
            return Array.Empty<NotificationDto>();

        var distinctIds = userIds.Distinct().ToList();
        var now = DateTime.UtcNow;

        var entities = distinctIds.Select(userId => new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = type,
            Title = title,
            Body = body,
            LinkUrl = linkUrl,
            IsRead = false,
            CreatedAt = now
        }).ToList();

        await _notifications.AddRangeAsync(entities, ct);
        await _notifications.SaveChangesAsync(ct);

        // Mỗi user một DTO (Id khác nhau) → publish từng người, song song.
        var dtos = entities.Select(MapToDto).ToList();
        await Task.WhenAll(entities.Select((n, i) =>
            _publisher.PublishToUserAsync(n.UserId, dtos[i], ct)));

        return dtos.AsReadOnly();
    }

    private static NotificationDto MapToDto(Notification n) =>
        new(n.Id, n.Type, n.Title, n.Body, n.LinkUrl, n.IsRead, n.CreatedAt);
}
