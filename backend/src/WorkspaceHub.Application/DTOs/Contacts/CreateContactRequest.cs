namespace WorkspaceHub.Application.DTOs.Contacts;

public class CreateContactRequest
{
    public Guid ConnectionId { get; set; }
    public string Email { get; set; } = null!;
    public string? DisplayName { get; set; }
}
