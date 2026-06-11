using WorkspaceHub.Application.Common;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.OAuth.Providers.Google;

internal static class GoogleScopes
{
    public const string OpenId = "openid";
    public const string Email = "email";
    public const string Profile = "profile";
    public static readonly string[] Login = { OpenId, Email, Profile };

    public const string GmailReadonly    = "https://www.googleapis.com/auth/gmail.readonly";
    public const string GmailSend        = "https://www.googleapis.com/auth/gmail.send";
    public const string CalendarReadonly = "https://www.googleapis.com/auth/calendar.readonly";
    public const string CalendarWrite    = "https://www.googleapis.com/auth/calendar";
    public const string DriveReadonly    = "https://www.googleapis.com/auth/drive.readonly";
    public const string DriveWrite       = "https://www.googleapis.com/auth/drive";

    // Toàn bộ scope bắt buộc — user phải grant đủ hết, thiếu 1 là reject.
    public static readonly string[] Required =
    [
        GmailReadonly, GmailSend,
        CalendarReadonly, CalendarWrite,
        DriveReadonly, DriveWrite,
    ];

    /// <summary>
    /// Kiểm tra scope Google trả về sau consent. Thiếu bất kỳ scope nào → throw BusinessRuleException.
    /// Đủ hết → trả danh sách ServiceType đã được kích hoạt.
    /// </summary>
    public static IReadOnlyList<ServiceType> ValidateAndExtract(string grantedScopes)
    {
        if (string.IsNullOrWhiteSpace(grantedScopes))
            throw new BusinessRuleException("Bạn cần cấp đầy đủ quyền cho: Gmail, Google Calendar, Google Drive");

        var set = grantedScopes
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = new List<string>();
        if (!set.Contains(GmailReadonly) || !set.Contains(GmailSend))
            missing.Add("Gmail");
        if (!set.Contains(CalendarReadonly) || !set.Contains(CalendarWrite))
            missing.Add("Google Calendar");
        if (!set.Contains(DriveReadonly) || !set.Contains(DriveWrite))
            missing.Add("Google Drive");

        if (missing.Count > 0)
            throw new BusinessRuleException($"Bạn cần cấp đầy đủ quyền cho: {string.Join(", ", missing)}");

        return [ServiceType.Gmail, ServiceType.GCal, ServiceType.Drive];
    }
}
