using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.DTOs.Contacts;

public class ContactSuggestionDto
{
    public string Email { get; set; } = null!;
    public string? DisplayName { get; set; }
    public GoogleContactSource Source { get; set; }
}
