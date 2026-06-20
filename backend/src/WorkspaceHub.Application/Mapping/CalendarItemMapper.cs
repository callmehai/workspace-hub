using System.Text.Json;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
namespace WorkspaceHub.Application.Mapping;
public class CalendarItemMapper : ICalendarItemMapper
{
    public Item ToItem(CalendarEventDto ev, Guid userId, Guid connectionId)
    {
        var metadata = new
        {
            start = ev.Start,
            end = ev.End,
            location = ev.Location,
            attendees = ev.Attendees,
            meetUrl = ev.MeetUrl
        };
        return new Item
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = ItemType.Event,
            Title = string.IsNullOrEmpty(ev.Title) ? "(Không có tiêu đề)" : ev.Title,
            Snippet = ev.Snippet ?? string.Empty,
            ExternalId = ev.Id,
            ConnectionId = connectionId,
            Status = ItemStatus.Inbox, // Mặc định vứt vào Inbox
            OccurredAt = ev.OccurredAt?.UtcDateTime ?? DateTime.UtcNow,
            IsImportant = false,
            IsArchived = false,
            MetadataJson = JsonSerializer.Serialize(metadata)
        };
    }
}