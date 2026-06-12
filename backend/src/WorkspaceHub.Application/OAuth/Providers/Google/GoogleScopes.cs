using WorkspaceHub.Application.Common;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.OAuth.Providers.Google;

internal static class GoogleScopes
{
    public const string OpenId = "openid";
    public const string Email = "email";
    public const string Profile = "profile";
    public static readonly string[] Login = [OpenId, Email, Profile];

    public const string GmailReadonly    = "https://www.googleapis.com/auth/gmail.readonly";
    public const string GmailSend        = "https://www.googleapis.com/auth/gmail.send";
    public const string CalendarReadonly = "https://www.googleapis.com/auth/calendar.readonly";
    public const string CalendarWrite    = "https://www.googleapis.com/auth/calendar";
    public const string DriveReadonly    = "https://www.googleapis.com/auth/drive.readonly";
    public const string DriveWrite       = "https://www.googleapis.com/auth/drive";

    // Toàn bộ scope bắt buộc — dùng làm reference, không dùng trực tiếp trong BuildForService nữa.
    public static readonly string[] Required =
    [
        GmailReadonly, GmailSend,
        CalendarReadonly, CalendarWrite,
        DriveReadonly, DriveWrite,
    ];

    // Map ServiceType → scopes cần request cho service đó (Google services only).
    public static readonly IReadOnlyDictionary<ServiceType, string[]> ServiceScopes =
        new Dictionary<ServiceType, string[]>
        {
            [ServiceType.Gmail] = [GmailReadonly, GmailSend],
            [ServiceType.GCal]  = [CalendarWrite],
            [ServiceType.Drive] = [DriveWrite],
        };

    /// <summary>Scopes request khi build auth URL — thêm openid+email để Google trả id_token (lấy email user).</summary>
    public static string[] BuildRequestScopes(ServiceType serviceType)
        => [OpenId, Email, .. ServiceScopes[serviceType]];

    /// <summary>
    /// Kiểm tra scope Google trả về sau consent cho 1 service cụ thể.
    /// Đủ scope của <paramref name="requested"/> → trả về <paramref name="requested"/>.
    /// Thiếu → throw BusinessRuleException.
    /// </summary>
    public static ServiceType ValidateAndExtract(string grantedScopes, ServiceType requested)
    {
        if (!ServiceScopes.TryGetValue(requested, out var requiredScopes))
            throw new BusinessRuleException($"Service '{requested}' không phải Google service");

        if (string.IsNullOrWhiteSpace(grantedScopes))
            throw new BusinessRuleException($"Bạn cần cấp đầy đủ quyền cho: {requested}");

        var set = grantedScopes
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = requiredScopes.Where(s => !set.Contains(s)).ToList();
        if (missing.Count > 0)
            throw new BusinessRuleException($"Bạn cần cấp đầy đủ quyền cho: {requested}");

        return requested;
    }
}
