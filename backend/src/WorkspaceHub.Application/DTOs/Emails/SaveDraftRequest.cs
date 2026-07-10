using System;
using System.Collections.Generic;

namespace WorkspaceHub.Application.DTOs.Emails;

public class SaveDraftRequest
{
    public Guid ConnectionId { get; set; }
    public List<string> To { get; set; } = new();
    public List<string> Cc { get; set; } = new();
    public List<string> Bcc { get; set; } = new();
    public string? Subject { get; set; }
    public string? BodyHtml { get; set; }
    public string? ThreadId { get; set; }
    public string? InReplyToMessageId { get; set; }
    public List<AttachmentUpload> Attachments { get; set; } = new();
}
