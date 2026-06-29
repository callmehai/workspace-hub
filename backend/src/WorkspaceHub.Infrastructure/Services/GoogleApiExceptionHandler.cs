using System;
using System.Net;
using Google;
using WorkspaceHub.Application.Common;

namespace WorkspaceHub.Infrastructure.Services;

/// <summary>
/// Shared handler cho GoogleApiException xuyên suốt các Gateway (Gmail/Calendar/Drive).
/// Tránh copy-paste cùng 1 block catch ở mỗi method.
///
/// Mapping:
///   404 → NotFoundException
///   403 / insufficientPermissions → ForbiddenException (gợi ý reconnect)
///   Còn lại → ProviderException (middleware map → 502)
/// </summary>
internal static class GoogleApiExceptionHandler
{
    /// <summary>
    /// Chuyển <see cref="GoogleApiException"/> thành domain exception tương ứng.
    /// </summary>
    /// <param name="ex">Exception từ Google SDK.</param>
    /// <param name="apiName">Tên API để hiển thị trong ProviderException (vd "Gmail", "Calendar", "Drive").</param>
    /// <param name="resourceType">Loại resource để hiển thị trong NotFoundException (vd "Message", "Event", "File").</param>
    /// <param name="resourceId">ID resource gây lỗi.</param>
    /// <param name="forbiddenMessage">Message tùy chỉnh cho ForbiddenException. Mặc định gợi ý reconnect.</param>
    public static Exception Handle(
        GoogleApiException ex,
        string apiName,
        string resourceType,
        string resourceId,
        string forbiddenMessage = "Cần reconnect với quyền ghi.")
    {
        if (ex.HttpStatusCode == HttpStatusCode.NotFound)
            return new NotFoundException(resourceType, resourceId);

        if (IsInsufficientPermissions(ex))
            return new ForbiddenException(forbiddenMessage);

        return new ProviderException($"{apiName} API error: {ex.Message}", ex);
    }

    private static bool IsInsufficientPermissions(GoogleApiException ex)
    {
        if (ex.HttpStatusCode == HttpStatusCode.Forbidden)
            return true;

        return ex.Error?.Errors?.Any(e =>
            e.Reason?.Contains("insufficientPermissions", StringComparison.OrdinalIgnoreCase) == true) == true;
    }
}
