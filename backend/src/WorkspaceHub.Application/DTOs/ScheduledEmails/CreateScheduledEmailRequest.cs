using System.ComponentModel.DataAnnotations;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.DTOs.ScheduledEmails;

public class CreateScheduledEmailRequest
{
    [Required]
    public Guid ConnectionId { get; set; }

    public List<string> To { get; set; } = new();
    public List<string> Cc { get; set; } = new();
    public List<string> Bcc { get; set; } = new();

    [Required]
    public string Subject { get; set; } = null!;

    [Required]
    public string BodyHtml { get; set; } = null!;

    [Required]
    public DateTime SendAt { get; set; }
}
