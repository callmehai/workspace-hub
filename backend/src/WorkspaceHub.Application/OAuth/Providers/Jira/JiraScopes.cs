using WorkspaceHub.Application.Common;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.OAuth.Providers.Jira;

internal static class JiraScopes
{
    public const string ReadJiraWork  = "read:jira-work";
    public const string WriteJiraWork = "write:jira-work";
    public const string ManageProject = "manage:jira-project";
    public const string ReadJiraUser  = "read:jira-user";
    public const string ReadMe        = "read:me";
    public const string OfflineAccess = "offline_access";

    // Toàn bộ scope bắt buộc — Jira không có fine-grained consent, user accept all or nothing.
    public static readonly string[] All =
    [
        ReadJiraWork, WriteJiraWork, ManageProject,
        ReadJiraUser, ReadMe, OfflineAccess
    ];

    /// <summary>
    /// Kiểm tra scope Atlassian trả về sau consent. Thiếu bất kỳ scope nào → throw BusinessRuleException.
    /// Đủ hết → trả [ServiceType.Jira].
    /// </summary>
    public static IReadOnlyList<ServiceType> ValidateAndExtract(string grantedScopes)
    {
        if (string.IsNullOrWhiteSpace(grantedScopes))
            throw new BusinessRuleException("Bạn cần cấp đầy đủ quyền cho Jira");

        var set = grantedScopes
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = All.Where(s => !set.Contains(s)).ToList();
        if (missing.Count > 0)
            throw new BusinessRuleException("Bạn cần cấp đầy đủ quyền cho Jira");

        return [ServiceType.Jira];
    }
}
