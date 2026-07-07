using System.Net;

namespace WorkspaceHub.Application.Common;

/// <summary>
/// Phân loại lỗi sync connection — dùng chung cho cron và on-demand.
/// </summary>
public static class ConnectionSyncExceptions
{
    /// <summary>
    /// Lỗi auth bền (token revoke, thiếu quyền) — nên đánh dấu connection Error.
    /// Lỗi tạm thời (5xx, rate limit) → false để retry.
    /// </summary>
    public static bool IsPersistentAuthFailure(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is ForbiddenException or UnauthorizedException)
                return true;

            if (current is ProviderException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden })
                return true;
        }

        return false;
    }
}
