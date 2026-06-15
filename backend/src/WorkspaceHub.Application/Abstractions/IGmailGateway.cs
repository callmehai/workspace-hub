using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Abstractions;

public interface IGmailGateway
{
    Task<GmailProfile> GetProfileAsync(Connection connection, CancellationToken ct = default);
    Task<GmailMessageList> ListMessageIdsAsync(Connection connection, string? pageToken, int maxResults, CancellationToken ct = default);
    Task<GmailMessage> GetMessageAsync(Connection connection, string messageId, CancellationToken ct = default);
}
