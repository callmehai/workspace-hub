using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Abstractions;

public interface ITokenService
{
    Task<string> GetFreshAccessTokenAsync(Connection connection, CancellationToken ct = default);
}
