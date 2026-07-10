using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.DTOs.Contacts;

/// <summary>Một dòng contact từ People API trước khi lưu DB (SCRUM-69).</summary>
public class PeopleContactRow
{
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public GoogleContactSource Source { get; set; }
    public string? ExternalResourceName { get; set; }
    public string? Etag { get; set; }
    public string? MetadataJson { get; set; }
}
