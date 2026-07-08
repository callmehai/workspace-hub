using WorkspaceHub.Application.DTOs.Emails;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Interfaces.Repositories;

public interface IGoogleContactRepository
{
    /// <summary>
    /// Ghi đè toàn bộ cache contact của một connection: xoá hết row cũ rồi insert batch mới
    /// (full replace sau mỗi lần sync — phản ánh đúng danh bạ Google hiện tại).
    /// </summary>
    Task ReplaceAllForConnectionAsync(Guid connectionId, IReadOnlyList<GoogleContact> contacts, CancellationToken ct = default);

    /// <summary>OData list — dedupe theo email, projection sang ContactSuggestionDto (in-memory).</summary>
    IQueryable<ContactSuggestionDto> GetByConnectionId(Guid connectionId);
}
