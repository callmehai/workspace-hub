using WorkspaceHub.Application.DTOs;

namespace WorkspaceHub.Application.Interfaces.Services;

/// <summary>
/// CRUD tag + gắn/gỡ tag khỏi item (SCRUM-70). Tag là label private của user (không share).
/// </summary>
public interface ITagService
{
    Task<IReadOnlyList<TagResponse>> GetAsync(Guid userId, CancellationToken ct = default);
    Task<TagResponse> CreateAsync(Guid userId, CreateTagRequest request, CancellationToken ct = default);
    Task<TagResponse> UpdateAsync(Guid userId, Guid id, UpdateTagRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default);

    /// <summary>Gắn tag vào item (cả tag lẫn item phải thuộc user). 409 nếu đã gắn.</summary>
    Task<TagAssignmentResponse> AssignAsync(Guid userId, Guid tagId, AssignTagRequest request, CancellationToken ct = default);

    /// <summary>Gỡ tag khỏi item. 404 nếu chưa gắn.</summary>
    Task UnassignAsync(Guid userId, Guid tagId, Guid itemId, CancellationToken ct = default);
}
