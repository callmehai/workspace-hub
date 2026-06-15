using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Abstractions
{
    public interface IGoogleCalendarGateway
    {
        Task<CalendarSyncResult> SyncEventsAsync(Connection connection, string? syncToken, CancellationToken ct = default);
    }
}
