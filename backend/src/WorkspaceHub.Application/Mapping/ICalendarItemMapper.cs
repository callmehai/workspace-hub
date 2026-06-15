using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Mapping
{
    public interface ICalendarItemMapper
    {
        Item ToItem(CalendarEventDto ev, Guid userId, Guid connectionId);
    }
}
