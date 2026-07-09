using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs.ScheduledEmails;
using WorkspaceHub.Domain.Entities;
namespace WorkspaceHub.Application.Interfaces.Repositories;

/// <summary>
/// Repository cho ScheduledEmail — thêm method xoá theo ConnectionId cho disconnect flow (SCRUM-14).
/// </summary>
public interface IScheduledEmailRepository : IGenericRepository<ScheduledEmail>
{
    /// <summary>OData list — scope userId server-side, map in-memory (JSON To/Cc/Bcc).</summary>
    IQueryable<ScheduledEmailDto> GetByUserId(Guid userId);

    /// <summary>
    /// Xoá tất cả ScheduledEmails trỏ vào connectionId (DB-level, không load vào memory).
    /// Dùng khi disconnect connection — tránh FK violation (NoAction ở DB).
    /// </summary>
    Task DeleteByConnectionIdAsync(Guid connectionId, CancellationToken ct = default);

    /// <summary>
    /// Lấy các email Pending đã tới hạn (SendAt &lt;= nowUtc) cho cron processor (SCRUM-31).
    /// Tracked (KHÔNG AsNoTracking) để service cập nhật Status/SentAt/RetryCount rồi SaveChanges.
    /// Sắp xếp theo SendAt tăng dần và giới hạn maxBatch để tránh xử lý quá tải 1 lượt.
    /// </summary>
    Task<IReadOnlyList<ScheduledEmail>> GetPendingDueEmailsAsync(DateTime nowUtc, int maxBatch, CancellationToken ct = default);
}
