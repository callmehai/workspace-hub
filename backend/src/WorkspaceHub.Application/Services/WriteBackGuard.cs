using Microsoft.Extensions.Logging;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Application.Services;

/// <summary>
/// Conflict guard chung cho mọi write-back lên provider (SCRUM-38).
///
/// Vai trò: <b>chỉ so sánh ETag</b> — KHÔNG gọi provider, KHÔNG chạm DB.
/// Caller (ItemWriteBackService) tự fetch ETag hiện tại từ provider ngay trước khi ghi,
/// rồi truyền cả ETag đã lưu (item.ETag) lẫn ETag provider vào đây.
///
/// Lệch → ném <see cref="ConflictException"/> → ExceptionMiddleware (SCRUM-24) map sang HTTP 409.
/// Caller KHÔNG tự set status code, KHÔNG tự throw — chỉ để exception bay lên.
/// Sau khi ghi thành công, caller tự cập nhật item.ETag = ETag mới (không phải việc của guard).
/// </summary>
public class WriteBackGuard : IWriteBackGuard
{
    private readonly ILogger<WriteBackGuard> _logger;

    public WriteBackGuard(ILogger<WriteBackGuard> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Quy ước skip-check (KHÔNG coi là conflict):
    /// <list type="bullet">
    ///   <item><c>storedEtag</c> null/empty: item chưa có version đối chiếu (vd vừa tạo, hoặc
    ///   provider không cấp ETag lúc sync) → cho phép ghi.</item>
    ///   <item><c>providerEtag</c> null/empty: provider không hỗ trợ ETag cho resource này
    ///   → không có gì để so → cho phép ghi.</item>
    /// </list>
    /// Chỉ khi cả hai cùng có giá trị mà lệch nhau mới là conflict.
    /// </remarks>
    public void EnsureNoConflict(string? storedEtag, string? providerEtag)
    {
        if (string.IsNullOrEmpty(storedEtag)) return;
        if (string.IsNullOrEmpty(providerEtag)) return;

        if (!string.Equals(storedEtag, providerEtag, StringComparison.Ordinal))
        {
            // Ghi log để có dấu vết debug ở production — exception chỉ mang TraceId.
            _logger.LogWarning(
                "Write-back ETag conflict: stored={StoredEtag}, provider={ProviderEtag}",
                storedEtag, providerEtag);
            throw new ConflictException(
                "The item was modified on the provider since it was last synced. " +
                "Refetch the latest version and retry.");
        }
    }
}
