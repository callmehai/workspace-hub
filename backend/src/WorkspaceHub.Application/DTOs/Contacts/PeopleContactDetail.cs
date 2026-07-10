namespace WorkspaceHub.Application.DTOs.Contacts;

/// <summary>Chi tiết contact từ People API (get / write-back).</summary>
public class PeopleContactDetail
{
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public string? Etag { get; set; }
    public string ResourceName { get; set; } = null!;
    public ContactProfileDto Profile { get; set; } = new();
}
