using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.OAuth.Providers.Jira;

internal static class JiraScopes
{
    // Classic scopes — đủ quyền read/write toàn bộ Jira project
    public const string ReadJiraWork    = "read:jira-work";
    public const string WriteJiraWork   = "write:jira-work";
    public const string ManageProject   = "manage:jira-project";
    public const string ReadJiraUser    = "read:jira-user";
    public const string OfflineAccess   = "offline_access";

    public static readonly string[] All =
    [
        ReadJiraWork,
        WriteJiraWork,
        ManageProject,
        ReadJiraUser,
        OfflineAccess
    ];

    public static IReadOnlyList<ServiceType> ServicesFromGrantedScopes(string grantedScopes)
    {
        if (string.IsNullOrWhiteSpace(grantedScopes)) return Array.Empty<ServiceType>();
        var set = grantedScopes
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<ServiceType>();
        // Chỉ cần read là đủ kích hoạt — write/manage là bonus nếu user grant
        if (set.Contains(ReadJiraWork))
            result.Add(ServiceType.Jira);
        return result;
    }
}
