using WorkspaceHub.Application.DTOs.Contacts;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Interfaces.Repositories;

public interface IGoogleContactRepository
{
    Task SyncForConnectionAsync(Guid connectionId, IReadOnlyList<GoogleContact> contacts, CancellationToken ct = default);

    IQueryable<ContactDto> GetQueryableByConnectionId(Guid connectionId);

    Task<GoogleContact?> GetByEmailForConnectionAsync(Guid connectionId, string email, CancellationToken ct = default);

    Task<GoogleContact?> GetByResourceNameForConnectionAsync(Guid connectionId, string resourceName, CancellationToken ct = default);

    Task<IReadOnlyList<GoogleContact>> ListForConnectionAsync(Guid connectionId, CancellationToken ct = default);

    Task<GoogleContact?> GetByIdForUserAsync(Guid id, Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<GoogleContact>> GetByResourceNameAsync(Guid connectionId, string resourceName, CancellationToken ct = default);

    Task UpsertAsync(GoogleContact contact, CancellationToken ct = default);

    /// <summary>Cập nhật row cache theo ExternalResourceName sau write-back (1 row / person).</summary>
    Task ApplyDetailToResourceAsync(
        Guid connectionId,
        string resourceName,
        PeopleContactDetail detail,
        DateTime updatedAt,
        CancellationToken ct = default);

    Task DeleteAsync(GoogleContact contact, CancellationToken ct = default);

    Task DeleteByResourceNameAsync(Guid connectionId, string resourceName, CancellationToken ct = default);
}
