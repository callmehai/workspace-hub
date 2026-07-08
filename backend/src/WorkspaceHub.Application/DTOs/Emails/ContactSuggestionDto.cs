namespace WorkspaceHub.Application.DTOs.Emails;

/// <summary>Gợi ý contact khi soạn To/Cc/Bcc (đọc từ cache DB).</summary>
public class ContactSuggestionDto
{
    public string Email { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string Source { get; set; } = null!;
}
