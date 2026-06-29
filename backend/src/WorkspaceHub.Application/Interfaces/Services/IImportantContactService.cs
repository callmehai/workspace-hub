using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Interfaces.Services;

/// <summary>
/// CRUD important contact (SCRUM-60): đánh dấu Email/JiraAccount là liên hệ quan trọng.
/// Item sync về từ contact này tự set IsImportant.
/// </summary>
public interface IImportantContactService
{
    Task<IReadOnlyList<ImportantContactResponse>> GetAsync(Guid userId, ImportantContactType? type, CancellationToken ct = default);
    Task<ImportantContactResponse> CreateAsync(Guid userId, CreateImportantContactRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default);
}
