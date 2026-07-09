using System.Net.Mail;
using System.Text.Json;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Entities;
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
            : newItems.Where(i => i.IsImportant).Take(maxPerSync).ToList();

        foreach (var item in toNotify)
        {
            var type = item.Type == ItemType.Email && item.IsImportant
                ? NotificationType.ImportantEmail
                : NotificationType.ItemSynced;

            var (titleKey, body) = BuildPayload(item);

            await _notifications.CreateAndSendAsync(
                userId, type, titleKey, body, $"/inbox?item={item.Id}", ct);
        }
    }

    private static (string TitleKey, string BodyJson) BuildPayload(Item item)
    {
        var preview = !string.IsNullOrWhiteSpace(item.Snippet) ? item.Snippet.Trim() : item.Title;
        var payload = new Dictionary<string, string>
        {
            ["preview"] = preview,
            ["itemTitle"] = item.Title,
        };

        var titleKey = item.Type switch
        {
            ItemType.Email when item.IsImportant => "notifications.importantEmailFrom",
            ItemType.Email => "notifications.newEmailFrom",
            ItemType.Event => "notifications.newEvent",
            ItemType.File => "notifications.newFile",
            ItemType.Ticket => "notifications.newTicket",
            _ => "notifications.newItem",
        };

        if (item.Type == ItemType.Email)
            payload["from"] = FormatEmailSender(TryGetMetadataString(item.MetadataJson, "from")) ?? item.Title;

        return (titleKey, JsonSerializer.Serialize(payload));
    }

    private static string? TryGetMetadataString(string metadataJson, string property)
    {
        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            if (doc.RootElement.TryGetProperty(property, out var prop))
                return prop.GetString();
        }
        catch (JsonException)
        {
            // Metadata không hợp lệ — bỏ qua.
        }

        return null;
    }

    private static string FormatEmailSender(string? fromHeader)
    {
        if (string.IsNullOrWhiteSpace(fromHeader))
            return "Unknown";

        try
        {
            var addr = new MailAddress(fromHeader);
            return string.IsNullOrWhiteSpace(addr.DisplayName) ? addr.Address : addr.DisplayName;
        }
        catch (FormatException)
        {
            return fromHeader.Trim();
        }
    }
}
