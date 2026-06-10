using WorkspaceHub.Application.Common;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Interfaces.Repositories;

/// <summary>Repository cho Integration — tra cứu provider catalog.</summary>
public interface IIntegrationRepository : IGenericRepository<Integration>
{
    Task<Integration?> GetByKeyAsync(string key, CancellationToken ct = default);
}
