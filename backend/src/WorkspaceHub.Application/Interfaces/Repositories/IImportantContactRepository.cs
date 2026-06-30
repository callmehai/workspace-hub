using WorkspaceHub.Application.Common;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Interfaces.Repositories;

public interface IImportantContactRepository : IGenericRepository<ImportantContact>
{
    Task<IReadOnlyList<string>> GetIdentifiersAsync(Guid userId, ImportantContactType type, CancellationToken ct = default);

    /// <summary>List contact của user, lọc theo type nếu có (SCRUM-60).</summary>
    Task<IReadOnlyList<ImportantContact>> GetByUserAsync(Guid userId, ImportantContactType? type = null, CancellationToken ct = default);

    /// <summary>Check trùng theo UNIQUE(UserId, Type, Identifier) trước khi tạo.</summary>
    Task<bool> ExistsAsync(Guid userId, ImportantContactType type, string identifier, CancellationToken ct = default);

    /// <summary>Lấy contact theo id + user (ownership check trước khi xoá).</summary>
    Task<ImportantContact?> GetByIdAndUserAsync(Guid id, Guid userId, CancellationToken ct = default);
}
