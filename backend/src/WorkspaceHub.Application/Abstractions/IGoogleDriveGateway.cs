using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Abstractions
{
    public interface IGoogleDriveGateway
    {
        Task<DriveSyncResult> SyncFilesAsync(Connection connection, string? pageToken, CancellationToken ct = default);
    }
}
