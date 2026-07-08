using WorkspaceHub.Application.DTOs.Contacts;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Abstractions;

public interface IPeopleGateway
{
    /// <summary>List đầy đủ connections + otherContacts (phân trang). Thiếu scope từng API → skip phần đó.</summary>
    Task<IReadOnlyList<PeopleContactRow>> ListAllAsync(Connection connection, CancellationToken ct = default);
}
