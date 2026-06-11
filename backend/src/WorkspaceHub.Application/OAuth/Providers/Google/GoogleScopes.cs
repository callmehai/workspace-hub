using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.OAuth.Providers.Google;

internal static class GoogleScopes
{
    public const string OpenId = "openid";
    public const string Email = "email";
    public const string Profile = "profile";
    public static readonly string[] Login = { OpenId, Email, Profile };

    public const string GmailReadonly = "https://www.googleapis.com/auth/gmail.readonly";
    public const string CalendarReadonly = "https://www.googleapis.com/auth/calendar.readonly";
    public const string DriveReadonly = "https://www.googleapis.com/auth/drive.readonly";

    public static string ForService(ServiceType service) => service switch
    {
        ServiceType.Gmail => GmailReadonly,
        ServiceType.GCal => CalendarReadonly,
        ServiceType.Drive => DriveReadonly,
        _ => throw new ArgumentOutOfRangeException(nameof(service), service, "Service không hỗ trợ")
    };

    public static IReadOnlyList<ServiceType> ServicesFromGrantedScopes(string grantedScopes)
    {
        if (string.IsNullOrWhiteSpace(grantedScopes)) return Array.Empty<ServiceType>();
        var set = grantedScopes
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<ServiceType>();
        if (set.Contains(GmailReadonly)) result.Add(ServiceType.Gmail);
        if (set.Contains(CalendarReadonly)) result.Add(ServiceType.GCal);
        if (set.Contains(DriveReadonly)) result.Add(ServiceType.Drive);
        return result;
    }
}
