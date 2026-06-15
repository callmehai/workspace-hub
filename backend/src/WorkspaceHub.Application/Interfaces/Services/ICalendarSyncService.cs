using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Interfaces.Services
{
    public interface ICalendarSyncService
    {
        Task<SyncResult> SyncAsync(Guid connectionId, Guid userId, CancellationToken ct = default);
        Task<SyncResult> SyncConnectionAsync(Connection connection, CancellationToken ct = default);
    }
}
