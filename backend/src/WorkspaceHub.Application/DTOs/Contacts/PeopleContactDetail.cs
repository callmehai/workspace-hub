namespace WorkspaceHub.Application.DTOs.Contacts;

/// <summary>Chi tiết contact từ People API (get / write-back).</summary>
public class PeopleContactDetail
{
    public string Email { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Etag { get; set; }
    public string ResourceName { get; set; } = null!;
}
