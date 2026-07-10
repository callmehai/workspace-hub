using WorkspaceHub.Application.DTOs.Contacts;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Abstractions;

public interface IPeopleGateway
{
    /// <summary>List đầy đủ connections + otherContacts (phân trang). Thiếu scope từng API → skip phần đó.</summary>
    Task<IReadOnlyList<PeopleContactRow>> ListAllAsync(Connection connection, CancellationToken ct = default);

    Task<PeopleContactDetail> GetContactAsync(Connection connection, string resourceName, CancellationToken ct = default);

    Task<PeopleContactDetail> CreateContactAsync(
        Connection connection, string email, string? displayName, CancellationToken ct = default);

    Task<PeopleContactDetail> UpdateContactAsync(
        Connection connection,
        string resourceName,
        string? etag,
        string email,
        string? displayName,
        CancellationToken ct = default);

    Task DeleteContactAsync(Connection connection, string resourceName, CancellationToken ct = default);
}
