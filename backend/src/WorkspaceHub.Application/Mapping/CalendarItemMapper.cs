using System.Text.Json;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Mapping;

public class CalendarItemMapper : ICalendarItemMapper
{
    public Item ToItem(
        CalendarEventDto ev,
        Guid userId,
        Guid connectionId,
        IReadOnlyDictionary<string, Guid>? driveFileIdToItemId = null)
    {
        var metadata = new Dictionary<string, object?>();
        if (ev.Start.HasValue)
            metadata["start"] = ev.AllDay ? ev.Start.Value.ToString("yyyy-MM-dd") : ev.Start.Value.UtcDateTime.ToString("o");
        if (ev.End.HasValue)
            metadata["end"] = ev.AllDay ? ev.End.Value.ToString("yyyy-MM-dd") : ev.End.Value.UtcDateTime.ToString("o");
        if (ev.AllDay) metadata["allDay"] = true;
        if (!string.IsNullOrEmpty(ev.Location)) metadata["location"] = ev.Location;
        if (!string.IsNullOrEmpty(ev.OrganizerEmail)) metadata["organizerEmail"] = ev.OrganizerEmail;
        if (!string.IsNullOrEmpty(ev.SelfResponseStatus)) metadata["selfResponseStatus"] = ev.SelfResponseStatus;
        if (ev.Attendees.Count > 0) metadata["attendees"] = ev.Attendees;
        if (!string.IsNullOrEmpty(ev.MeetUrl)) metadata["meetUrl"] = ev.MeetUrl;
        if (!string.IsNullOrEmpty(ev.HtmlLink)) metadata["htmlLink"] = ev.HtmlLink;
        if (!string.IsNullOrEmpty(ev.Snippet)) metadata["description"] = ev.Snippet;

        if (ev.Recurrence != null && ev.Recurrence.Count > 0) metadata["recurrence"] = ev.Recurrence;

        if (ev.DriveAttachments.Count > 0)
        {
            metadata["driveAttachments"] = ev.DriveAttachments
                .Where(a => !string.IsNullOrEmpty(a.FileId) || !string.IsNullOrEmpty(a.FileUrl))
                .Select(a => new { a.FileId, a.Title, a.MimeType, a.FileUrl })
                .ToList();

            if (driveFileIdToItemId != null)
            {
                var driveItemIds = ev.DriveAttachments
                    .Select(a => a.FileId)
                    .Where(id => !string.IsNullOrEmpty(id) && driveFileIdToItemId.ContainsKey(id!))
                    .Select(id => driveFileIdToItemId[id!].ToString())
                    .Distinct()
                    .ToList();

                if (driveItemIds.Count > 0)
                    metadata["driveItemIds"] = driveItemIds;
            }
        }

        return new Item
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = ItemType.Event,
            Title = string.IsNullOrEmpty(ev.Title) ? "(Không có tiêu đề)" : ev.Title,
            Snippet = ev.Snippet ?? string.Empty,
            ExternalId = ev.Id,
            ETag = ev.ETag,
            ConnectionId = connectionId,
            Status = ItemStatus.Inbox,
            OccurredAt = ev.Start?.UtcDateTime ?? DateTime.UtcNow,
            DueAt = ev.End?.UtcDateTime,
            IsImportant = false,
            IsArchived = false,
            MetadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
        };
    }
}
