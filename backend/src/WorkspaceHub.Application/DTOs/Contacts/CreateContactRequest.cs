namespace WorkspaceHub.Application.DTOs.Contacts;

public class CreateContactRequest
{
    public Guid ConnectionId { get; set; }
    public string Email { get; set; } = null!;
    /// <summary>Legacy simple create — dùng khi không gửi Profile.</summary>
    public string? DisplayName { get; set; }
    public ContactProfileDto? Profile { get; set; }
}
