using WorkspaceHub.Application.Common;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Interfaces.Repositories;

/// <summary>
/// Repository cho Tag (SCRUM-70). Tag là label private của user, gắn cho Item qua TagAssignment (m-n).
/// </summary>
public interface ITagRepository : IGenericRepository<Tag>
{
    /// <summary>List tag của user kèm số item đang gắn (ItemCount), sắp theo Name.</summary>
    Task<IReadOnlyList<(Tag Tag, int ItemCount)>> GetByUserWithCountAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Lấy tag theo id + user (ownership check trước khi update/delete/assign).</summary>
    Task<Tag?> GetByIdAndUserAsync(Guid id, Guid userId, CancellationToken ct = default);

    /// <summary>Check trùng tên trong phạm vi 1 user (case-insensitive). excludeId để bỏ chính nó khi update.</summary>
    Task<bool> NameExistsAsync(Guid userId, string name, Guid? excludeId = null, CancellationToken ct = default);

    // ── TagAssignment (junction) ──
    Task<bool> AssignmentExistsAsync(Guid tagId, Guid itemId, CancellationToken ct = default);
    Task AddAssignmentAsync(TagAssignment assignment, CancellationToken ct = default);
    Task<TagAssignment?> GetAssignmentAsync(Guid tagId, Guid itemId, CancellationToken ct = default);
    void RemoveAssignment(TagAssignment assignment);
}
