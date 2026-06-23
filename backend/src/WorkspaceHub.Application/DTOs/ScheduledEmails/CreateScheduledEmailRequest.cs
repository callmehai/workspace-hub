namespace WorkspaceHub.Application.DTOs.ScheduledEmails;

public class CreateScheduledEmailRequest
{
    public Guid ConnectionId { get; set; }

    public List<string> To { get; set; } = new();
    public List<string> Cc { get; set; } = new();
    public List<string> Bcc { get; set; } = new();

    public string Subject { get; set; } = null!;

    public string BodyHtml { get; set; } = null!;

    public DateTime SendAt { get; set; }
}
