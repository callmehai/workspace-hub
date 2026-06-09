using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Domain.Entities;

/// <summary>Item sync về có "from" match list → tự set IsImportant=true. MVP chỉ Email.</summary>
public class ImportantContact
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public ImportantContactType Type { get; set; } = ImportantContactType.Email;
    public string Identifier { get; set; } = null!;  // vd boss@company.com
    public string Label { get; set; } = null!;       // vd "Sếp Tổng"

    // Navigation
    public User User { get; set; } = null!;
}
