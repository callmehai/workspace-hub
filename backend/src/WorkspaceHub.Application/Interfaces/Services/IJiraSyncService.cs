using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Interfaces.Services;

public interface IJiraSyncService
{
    /// <summary>
    /// Sync on-demand issue Jira về Item(Type=Ticket): JQL search + phân trang + dedupe.
    /// Cursor (CursorType.JqlUpdated) lưu mốc fields.updated gần nhất; lần sau chỉ kéo issue updated &gt;= mốc.
    /// </summary>
    Task<SyncResult> SyncConnectionAsync(Connection connection, int maxIssues = 50, CancellationToken ct = default);
}
