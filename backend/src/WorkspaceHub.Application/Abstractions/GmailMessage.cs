using System;
using System.Collections.Generic;

namespace WorkspaceHub.Application.Abstractions;

public record GmailMessage(
    string Id,
    string ThreadId,
    string? Subject,
    string? From,
    IReadOnlyList<string> To,
    string? Snippet,
    IReadOnlyList<string> LabelIds,
    bool HasAttachment,
    DateTimeOffset? OccurredAt);

public record GmailMessageList(IReadOnlyList<string> MessageIds, string? NextPageToken);
