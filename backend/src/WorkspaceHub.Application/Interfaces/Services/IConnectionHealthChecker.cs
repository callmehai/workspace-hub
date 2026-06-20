namespace WorkspaceHub.Application.Interfaces.Services;

/// <summary>
/// On-demand sync: kiểm tra connection healthy (Active+Enabled, token valid),
/// auto-refresh nếu cần, rồi sync dữ liệu. Debounce theo config.
/// </summary>
public interface IConnectionHealthChecker
{
    /// <summary>
    /// Kiểm tra và sync TẤT CẢ active connections của user.
    /// Dùng khi mở unified Items list (GET /api/items).
    /// Debounce: skip sync nếu LastSyncedAt < X giây trước.
    /// Mỗi connection lỗi KHÔNG chặn các connection khác.
    /// </summary>
    Task EnsureAllSyncedAsync(Guid userId, CancellationToken ct = default);
}
