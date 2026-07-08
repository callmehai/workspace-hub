using System;
using System.Collections.Generic;

namespace WorkspaceHub.Application.Abstractions;

public record GmailMessage(
    string Id,
    string ThreadId,
    string? Subject,
    string? From,
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    IReadOnlyList<string> Bcc,
    string? Snippet,
    IReadOnlyList<string> LabelIds,
    bool HasAttachment,
    DateTimeOffset? OccurredAt,
    string? ETag = null,
    string? Rfc822MessageId = null,
    string? BodyHtml = null,
    string? BodyPlain = null);

public record GmailMessageList(IReadOnlyList<string> MessageIds, string? NextPageToken);
