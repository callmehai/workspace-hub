using WorkspaceHub.Application.Common;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Interfaces.Repositories;

public interface IImportantContactRepository : IGenericRepository<ImportantContact>
{
    Task<IReadOnlyList<string>> GetIdentifiersAsync(Guid userId, ImportantContactType type, CancellationToken ct = default);
}
