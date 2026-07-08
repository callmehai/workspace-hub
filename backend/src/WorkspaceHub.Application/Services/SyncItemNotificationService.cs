using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Services;

public class SyncItemNotificationService : ISyncItemNotificationService
{
    private readonly IItemRepository _items;
    private readonly INotificationService _notifications;

    public SyncItemNotificationService(IItemRepository items, INotificationService notifications)
    {
        _items = items;
        _notifications = notifications;
    }

    public async Task NotifyNewItemsAsync(
        Guid connectionId,
        Guid userId,
        IReadOnlySet<string> beforeExternalIds,
        CancellationToken ct = default)
    {
        var after = await _items.GetTrackedByConnectionIdAsync(connectionId, ct);
        var newItems = after.Values
            .Where(i => i.ExternalId is not null && !beforeExternalIds.Contains(i.ExternalId))
            .ToList();

        if (newItems.Count == 0)
            return;

        const int maxPerSync = 10;
        var toNotify = newItems.Count <= maxPerSync
            ? newItems
            : newItems.Where(i => i.IsImportant).ToList();

        foreach (var item in toNotify)
        {
            var type = item.Type == ItemType.Email && item.IsImportant
                ? NotificationType.ImportantEmail
                : NotificationType.ItemSynced;

            var title = item.Type switch
            {
                ItemType.Email => item.IsImportant ? $"Email quan trọng: {item.Title}" : $"Email mới: {item.Title}",
                ItemType.Event => $"Sự kiện mới: {item.Title}",
                ItemType.File => $"File mới: {item.Title}",
                ItemType.Ticket => $"Ticket mới: {item.Title}",
                _ => $"Mục mới: {item.Title}",
            };

            await _notifications.CreateAndSendAsync(
                userId, type, title, item.Snippet ?? string.Empty, $"/inbox?item={item.Id}", ct);
        }
    }
}
