using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Abstractions;

public interface IGmailGateway
{
    Task<GmailProfile> GetProfileAsync(Connection connection, CancellationToken ct = default);
}
