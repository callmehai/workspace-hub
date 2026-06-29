using WorkspaceHub.Application.Abstractions;

namespace WorkspaceHub.Application.Interfaces.Services;

/// <summary>
/// Helper đọc danh mục Jira (SCRUM-59) phục vụ FE render dropdown khi tạo/sửa ticket.
/// Mọi method validate connection thuộc user + ServiceType=Jira + Active trước khi gọi provider.
/// </summary>
public interface IJiraMetadataService
{
    Task<IReadOnlyList<JiraProject>> GetProjectsAsync(Guid connectionId, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<JiraIssueType>> GetIssueTypesAsync(Guid connectionId, Guid userId, string projectKey, CancellationToken ct = default);
    Task<IReadOnlyList<JiraPriority>> GetPrioritiesAsync(Guid connectionId, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<JiraUser>> GetAssignableUsersAsync(Guid connectionId, Guid userId, string projectKey, string? query, CancellationToken ct = default);

    /// <summary>Transition khả dụng của 1 issue (theo itemId local). Không cache (phụ thuộc workflow state).</summary>
    Task<IReadOnlyList<JiraTransition>> GetTransitionsAsync(Guid connectionId, Guid userId, Guid itemId, CancellationToken ct = default);
}
