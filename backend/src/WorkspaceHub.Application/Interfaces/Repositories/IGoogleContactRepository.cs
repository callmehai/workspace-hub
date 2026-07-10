using WorkspaceHub.Application.DTOs.Contacts;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Interfaces.Repositories;

public interface IGoogleContactRepository
{
    /// <summary>
    /// Đồng bộ cache contact của một connection: upsert theo ExternalResourceName hoặc Email,
    /// giữ Id/UpdatedAt row cũ; xoá row không còn trên Google.
    /// </summary>
    Task SyncForConnectionAsync(Guid connectionId, IReadOnlyList<GoogleContact> contacts, CancellationToken ct = default);

    /// <summary>OData list — projection SQL, scope theo connectionId ở service layer.</summary>
    IQueryable<ContactDto> GetQueryableByConnectionId(Guid connectionId);

    Task<GoogleContact?> GetByEmailForConnectionAsync(Guid connectionId, string email, CancellationToken ct = default);

    Task<GoogleContact?> GetByIdForUserAsync(Guid id, Guid userId, CancellationToken ct = default);

    Task UpsertAsync(GoogleContact contact, CancellationToken ct = default);

    Task DeleteAsync(GoogleContact contact, CancellationToken ct = default);
}

