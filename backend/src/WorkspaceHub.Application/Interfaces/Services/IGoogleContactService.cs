using WorkspaceHub.Application.DTOs.Contacts;

namespace WorkspaceHub.Application.Interfaces.Services;

public interface IGoogleContactService
{
    /// <summary>OData list — validate Gmail connection rồi trả IQueryable EF (SQL push-down).</summary>
    Task<IQueryable<ContactDto>> GetQueryableAsync(
        Guid userId,
        Guid connectionId,
        CancellationToken ct = default);

    Task<ContactDto> CreateAsync(Guid userId, CreateContactRequest request, CancellationToken ct = default);

    Task<ContactDto> UpdateAsync(Guid userId, Guid id, PatchContactRequest request, CancellationToken ct = default);

    Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default);
}
