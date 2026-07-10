namespace WorkspaceHub.Application.DTOs.Contacts;

public class PatchContactRequest
{
    public string Etag { get; set; } = null!;
    /// <summary>Legacy — dùng khi không gửi Profile.</summary>
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public ContactProfileDto? Profile { get; set; }
}
