using WorkspaceHub.Application.Common;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.OAuth.Providers.Google;

internal static class GoogleScopes
{
    public const string OpenId = "openid";
    public const string Email = "email";
    public const string Profile = "profile";
    public static readonly string[] Login = [OpenId, Email, Profile];

    public const string GmailReadonly     = "https://www.googleapis.com/auth/gmail.readonly";
    public const string GmailModify       = "https://www.googleapis.com/auth/gmail.modify";
    public const string GmailSend         = "https://www.googleapis.com/auth/gmail.send";
    public const string GmailSettingsBasic = "https://www.googleapis.com/auth/gmail.settings.basic";
    public const string CalendarReadonly = "https://www.googleapis.com/auth/calendar.readonly";
    public const string CalendarWrite    = "https://www.googleapis.com/auth/calendar";
    public const string DriveReadonly    = "https://www.googleapis.com/auth/drive.readonly";
    public const string DriveWrite       = "https://www.googleapis.com/auth/drive";

    // Toàn bộ scope bắt buộc — dùng làm reference, không dùng trực tiếp trong BuildForService nữa.
    public static readonly string[] Required =
    [
        GmailModify, GmailSend,
        CalendarReadonly, CalendarWrite,
        DriveReadonly, DriveWrite,
    ];

    // Map ServiceType → scopes BẮT BUỘC cho service đó (dùng để validate consent).
    public static readonly IReadOnlyDictionary<ServiceType, string[]> ServiceScopes =
        new Dictionary<ServiceType, string[]>
        {
            [ServiceType.Gmail] = [GmailModify, GmailSend],
            [ServiceType.GCal]  = [CalendarWrite],
            [ServiceType.Drive] = [DriveWrite],
        };

    // Scopes TUỲ CHỌN — request thêm để nâng trải nghiệm, KHÔNG bắt buộc để connect.
    // gmail.settings.basic: đọc chữ ký Gmail (SendEmail). Thiếu → chỉ mất chữ ký, không chặn connect.
    public static readonly IReadOnlyDictionary<ServiceType, string[]> OptionalServiceScopes =
        new Dictionary<ServiceType, string[]>
        {
            [ServiceType.Gmail] = [GmailSettingsBasic],
        };

    /// <summary>Scopes request khi build auth URL — openid+email (id_token) + bắt buộc + tuỳ chọn của service.</summary>
    public static string[] BuildRequestScopes(ServiceType serviceType)
    {
        var optional = OptionalServiceScopes.TryGetValue(serviceType, out var o) ? o : [];
        return [OpenId, Email, .. ServiceScopes[serviceType], .. optional];
    }

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
