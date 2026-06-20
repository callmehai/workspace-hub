using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Abstractions;

public interface IGmailGateway
{
    Task<GmailProfile> GetProfileAsync(Connection connection, CancellationToken ct = default);
    Task<GmailMessageList> ListMessageIdsAsync(Connection connection, string? pageToken, int maxResults, CancellationToken ct = default);
    Task<GmailMessage> GetMessageAsync(Connection connection, string messageId, CancellationToken ct = default);
    Task<GmailHistory> ListHistoryAsync(Connection connection, string startHistoryId, string? pageToken, CancellationToken ct = default);
    Task ModifyMessageAsync(Connection connection, string messageId, IList<string> addLabelIds, IList<string> removeLabelIds, CancellationToken ct = default);
    Task TrashMessageAsync(Connection connection, string messageId, CancellationToken ct = default);
    Task UntrashMessageAsync(Connection connection, string messageId, CancellationToken ct = default);
    Task<string?> GetMessageETagAsync(Connection connection, string messageId, CancellationToken ct = default);
}
