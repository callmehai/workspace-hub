namespace WorkspaceHub.Application.DTOs.Contacts;

public class PatchContactRequest
{
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string Etag { get; set; } = null!;
}
