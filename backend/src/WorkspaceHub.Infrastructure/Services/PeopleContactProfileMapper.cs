using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Apis.PeopleService.v1.Data;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs.Contacts;

namespace WorkspaceHub.Infrastructure.Services;

internal static class PeopleContactProfileMapper
{
    internal static ContactProfileDto FromPerson(Person person)
    {
        var name = person.Names?.FirstOrDefault();
        var profile = new ContactProfileDto
        {
            GivenName = name?.GivenName,
            FamilyName = name?.FamilyName,
        };

        if (person.EmailAddresses != null)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var addr in person.EmailAddresses)
            {
                if (string.IsNullOrWhiteSpace(addr.Value)) continue;
                var value = addr.Value.Trim().ToLowerInvariant();
                if (!seen.Add(value)) continue;
                profile.Emails.Add(new LabeledEmailDto
                {
                    Value = value,
                    Label = NormalizeLabel(addr.Type),
                });
            }
        }

        if (person.PhoneNumbers != null)
        {
            foreach (var phone in person.PhoneNumbers)
            {
                if (string.IsNullOrWhiteSpace(phone.Value)) continue;
                profile.Phones.Add(new LabeledPhoneDto
                {
                    Value = phone.Value.Trim(),
                    Label = NormalizeLabel(phone.Type),
                });
            }
        }

        var bday = person.Birthdays?.FirstOrDefault()?.Date;
        if (bday?.Month is > 0 && bday.Day is > 0)
        {
            profile.Birthday = new ContactBirthdayDto
            {
                Month = (int?)bday.Month,
                Day = (int?)bday.Day,
                Year = bday.Year,
            };
        }

        var org = person.Organizations?.FirstOrDefault();
        if (org != null && (!string.IsNullOrWhiteSpace(org.Name) || !string.IsNullOrWhiteSpace(org.Title)))
        {
            profile.Organization = new ContactOrganizationDto
            {
                Name = org.Name,
                Title = org.Title,
            };
        }

        return profile;
    }

    internal static string? ResolveDisplayName(Person person, ContactProfileDto profile) =>
        ContactProfileJson.ComputeDisplayName(profile) ?? ExtractDisplayName(person);

    internal static string? ExtractDisplayName(Person person) =>
        ExtractDisplayName(person.Names?.FirstOrDefault());

    internal static string? ExtractDisplayName(Name? name)
    {
        if (name == null) return null;

        if (!string.IsNullOrWhiteSpace(name.DisplayName))
            return name.DisplayName.Trim();

        if (!string.IsNullOrWhiteSpace(name.UnstructuredName))
            return name.UnstructuredName.Trim();

        var parts = new[] { name.GivenName, name.MiddleName, name.FamilyName }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim())
            .ToArray();

        return parts.Length > 0 ? string.Join(' ', parts) : null;
    }

    internal static void ApplyProfile(Person target, ContactProfileDto profile, Person? existing)
    {
        var existingName = existing?.Names?.FirstOrDefault();
        var given = profile.GivenName?.Trim();
        var family = profile.FamilyName?.Trim();

        if (string.IsNullOrWhiteSpace(given))
            given = existingName?.GivenName;
        if (string.IsNullOrWhiteSpace(family))
            family = existingName?.FamilyName;

        if (!string.IsNullOrWhiteSpace(given) || !string.IsNullOrWhiteSpace(family))
        {
            var display = string.Join(' ', new[] { given, family }.Where(s => !string.IsNullOrWhiteSpace(s)));
            target.Names =
            [
                new Name
                {
                    GivenName = given,
                    FamilyName = family,
                    DisplayName = string.IsNullOrWhiteSpace(display) ? null : display,
                }
            ];
        }
        else if (existingName != null)
        {
            target.Names = [CloneName(existingName)];
        }

        var emails = profile.Emails
            .Where(e => !string.IsNullOrWhiteSpace(e.Value))
            .Select(e => new EmailAddress
            {
                Value = e.Value.Trim().ToLowerInvariant(),
                Type = DenormalizeLabel(e.Label),
            })
            .ToList();
        target.EmailAddresses = emails.Count > 0 ? emails : null;

        var phones = profile.Phones
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .Select(p => new PhoneNumber
            {
                Value = p.Value.Trim(),
                Type = DenormalizeLabel(p.Label),
            })
            .ToList();
        target.PhoneNumbers = phones.Count > 0 ? phones : null;

        if (profile.Birthday?.Month is > 0 && profile.Birthday.Day is > 0)
        {
            target.Birthdays =
            [
                new Birthday
                {
                    Date = new Date
                    {
                        Month = profile.Birthday.Month,
                        Day = profile.Birthday.Day,
                        Year = profile.Birthday.Year,
                    },
                }
            ];
        }
        else if (existing?.Birthdays != null)
        {
            target.Birthdays = existing.Birthdays;
        }
        else
        {
            target.Birthdays = null;
        }

        if (profile.Organization != null
            && (!string.IsNullOrWhiteSpace(profile.Organization.Name) || !string.IsNullOrWhiteSpace(profile.Organization.Title)))
        {
            target.Organizations =
            [
                new Organization
                {
                    Name = profile.Organization.Name?.Trim(),
                    Title = profile.Organization.Title?.Trim(),
                }
            ];
        }
        else if (existing?.Organizations != null)
        {
            target.Organizations = existing.Organizations;
        }
        else
        {
            target.Organizations = null;
        }
    }

    internal static ContactProfileDto FromJson(string? json) => ContactProfileJson.Deserialize(json);

    internal static string? ToJson(ContactProfileDto profile) => ContactProfileJson.Serialize(profile);

    internal static string? ComputeDisplayName(ContactProfileDto profile) => ContactProfileJson.ComputeDisplayName(profile);

    internal static string? PrimaryEmail(ContactProfileDto profile, string? fallback = null) =>
        ContactProfileJson.PrimaryEmail(profile, fallback);

    internal static ContactProfileDto BuildSimple(string email, string? displayName) =>
        ContactProfileJson.BuildSimple(email, displayName);

    private static Name CloneName(Name source) => new()
    {
        DisplayName = source.DisplayName,
        GivenName = source.GivenName,
        FamilyName = source.FamilyName,
        MiddleName = source.MiddleName,
        HonorificPrefix = source.HonorificPrefix,
        HonorificSuffix = source.HonorificSuffix,
        UnstructuredName = source.UnstructuredName,
    };

    private static string? NormalizeLabel(string? type) =>
        string.IsNullOrWhiteSpace(type) ? null : type.Trim().ToLowerInvariant();

    private static string? DenormalizeLabel(string? label) =>
        string.IsNullOrWhiteSpace(label) ? null : label.Trim().ToLowerInvariant();
}
