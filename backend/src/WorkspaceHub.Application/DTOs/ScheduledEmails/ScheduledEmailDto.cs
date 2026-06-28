using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.DTOs.ScheduledEmails;

public class ScheduledEmailDto
{
    public Guid Id { get; set; }
    public Guid ConnectionId { get; set; }
    public List<string> To { get; set; } = new();
    public List<string> Cc { get; set; } = new();
    public List<string> Bcc { get; set; } = new();
    public string Subject { get; set; } = null!;
    public string BodyHtml { get; set; } = null!;
    public DateTime SendAt { get; set; }
    public string Status { get; set; } = null!;
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public DateTime? SentAt { get; set; }
}
