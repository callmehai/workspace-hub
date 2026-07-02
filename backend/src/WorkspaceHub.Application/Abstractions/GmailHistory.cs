using System.Collections.Generic;

namespace WorkspaceHub.Application.Abstractions;

public record GmailHistory(
    bool Expired,
    IReadOnlyList<string> AffectedMessageIds,
    string? NextPageToken,
    string? LatestHistoryId);
