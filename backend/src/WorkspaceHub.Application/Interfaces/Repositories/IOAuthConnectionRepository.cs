using WorkspaceHub.Application.Common;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Interfaces.Repositories;

/// <summary>Repository cho OAuthConnection — lưu grant OAuth của user.</summary>
public interface IOAuthConnectionRepository : IGenericRepository<OAuthConnection>
{
    Task<OAuthConnection?> GetByUniqueKeyAsync(
        Guid userId,
        Guid integrationId,
        string providerAccountId,
        CancellationToken ct = default);
}
