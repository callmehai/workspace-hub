using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Mapping;

public interface ICalendarItemMapper
{
    Item ToItem(
        CalendarEventDto ev,
        Guid userId,
        Guid connectionId,
        IReadOnlyDictionary<string, Guid>? driveFileIdToItemId = null);
}
