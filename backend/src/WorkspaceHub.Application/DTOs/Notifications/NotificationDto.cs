using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.DTOs.Notifications;

public record NotificationDto(
    Guid Id,
    NotificationType Type,
    string Title,
    string Body,
    string LinkUrl,
    bool IsRead,
    DateTime CreatedAt);
