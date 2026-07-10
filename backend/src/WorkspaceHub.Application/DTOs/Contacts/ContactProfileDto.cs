namespace WorkspaceHub.Application.DTOs.Contacts;

public class ContactProfileDto
{
    public string? GivenName { get; set; }
    public string? FamilyName { get; set; }
    public List<LabeledEmailDto> Emails { get; set; } = [];
    public List<LabeledPhoneDto> Phones { get; set; } = [];
    public ContactBirthdayDto? Birthday { get; set; }
    public ContactOrganizationDto? Organization { get; set; }
}

public class LabeledEmailDto
{
    public string Value { get; set; } = null!;
    public string? Label { get; set; }
}

public class LabeledPhoneDto
{
    public string Value { get; set; } = null!;
    public string? Label { get; set; }
}

public class ContactBirthdayDto
{
    public int? Month { get; set; }
    public int? Day { get; set; }
    public int? Year { get; set; }
}

public class ContactOrganizationDto
{
    public string? Name { get; set; }
    public string? Title { get; set; }
}
