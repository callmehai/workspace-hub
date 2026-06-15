using WorkspaceHub.Application.Common;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Interfaces.Repositories;

/// <summary>
/// Repository cho ScheduledEmail — thêm method xoá theo ConnectionId cho disconnect flow (SCRUM-14).
/// </summary>
public interface IScheduledEmailRepository : IGenericRepository<ScheduledEmail>
{
    /// <summary>
    /// Xoá tất cả ScheduledEmails trỏ vào connectionId (DB-level, không load vào memory).
    /// Dùng khi disconnect connection — tránh FK violation (NoAction ở DB).
    /// </summary>
    Task DeleteByConnectionIdAsync(Guid connectionId, CancellationToken ct = default);
}
