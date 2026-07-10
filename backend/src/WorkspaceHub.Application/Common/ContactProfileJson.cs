using System.Text.Json;
using System.Text.Json.Serialization;
using WorkspaceHub.Application.DTOs.Contacts;

namespace WorkspaceHub.Application.Common;

public static class ContactProfileJson
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static ContactProfileDto Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ContactProfileDto();
        try
        {
            return JsonSerializer.Deserialize<ContactProfileDto>(json, JsonOpts) ?? new ContactProfileDto();
        }
        catch
        {
            return new ContactProfileDto();
        }
    }

    public static string? Serialize(ContactProfileDto profile)
    {
        if (profile.Emails.Count == 0 && profile.Phones.Count == 0 && profile.Birthday == null
            && profile.Organization == null && string.IsNullOrWhiteSpace(profile.GivenName)
            && string.IsNullOrWhiteSpace(profile.FamilyName))
            return null;

        return JsonSerializer.Serialize(profile, JsonOpts);
    }

    public static string? ComputeDisplayName(ContactProfileDto profile)
    {
        var parts = new[] { profile.GivenName, profile.FamilyName }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim())
            .ToArray();
        return parts.Length > 0 ? string.Join(' ', parts) : null;
    }

    public static string? PrimaryEmail(ContactProfileDto profile, string? fallback = null)
    {
        var resolved = ResolvePrimaryEmail(profile);
        return resolved ?? (string.IsNullOrWhiteSpace(fallback) ? null : fallback.Trim().ToLowerInvariant());
    }

    /// <summary>Ưu tiên label work → home → other → phần tử đầu.</summary>
    public static string? ResolvePrimaryEmail(ContactProfileDto profile)
    {
        var emails = profile.Emails
            .Where(e => !string.IsNullOrWhiteSpace(e.Value))
            .Select(e => new LabeledEmailDto
            {
                Value = e.Value.Trim().ToLowerInvariant(),
                Label = string.IsNullOrWhiteSpace(e.Label) ? null : e.Label.Trim().ToLowerInvariant(),
            })
            .ToList();

        if (emails.Count == 0) return null;

        var byLabel = new[] { "work", "home", "other" };
        foreach (var label in byLabel)
        {
            var match = emails.FirstOrDefault(e => e.Label == label);
            if (match != null) return match.Value;
        }

        return emails[0].Value;
    }

    public static ContactProfileDto Normalize(ContactProfileDto profile)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var emails = new List<LabeledEmailDto>();
        foreach (var entry in profile.Emails)
        {
            if (string.IsNullOrWhiteSpace(entry.Value)) continue;
            var value = entry.Value.Trim().ToLowerInvariant();
            if (!seen.Add(value)) continue;
            emails.Add(new LabeledEmailDto
            {
                Value = value,
                Label = string.IsNullOrWhiteSpace(entry.Label) ? null : entry.Label.Trim().ToLowerInvariant(),
            });
        }

        return new ContactProfileDto
        {
            GivenName = string.IsNullOrWhiteSpace(profile.GivenName) ? null : profile.GivenName.Trim(),
            FamilyName = string.IsNullOrWhiteSpace(profile.FamilyName) ? null : profile.FamilyName.Trim(),
            Emails = emails,
            Phones = profile.Phones
                .Where(p => !string.IsNullOrWhiteSpace(p.Value))
                .Select(p => new LabeledPhoneDto
                {
                    Value = p.Value.Trim(),
                    Label = string.IsNullOrWhiteSpace(p.Label) ? null : p.Label.Trim().ToLowerInvariant(),
                })
                .ToList(),
            Birthday = profile.Birthday,
            Organization = profile.Organization,
        };
    }

    public static bool HasEmail(ContactProfileDto profile) =>
        profile.Emails.Any(e => !string.IsNullOrWhiteSpace(e.Value));

    public static ContactProfileDto BuildSimple(string email, string? displayName)
    {
        var profile = new ContactProfileDto
        {
            Emails = [new LabeledEmailDto { Value = email.Trim().ToLowerInvariant() }],
        };
        if (!string.IsNullOrWhiteSpace(displayName))
            profile.GivenName = displayName.Trim();
        return profile;
    }

    public static ContactProfileDto ResolveForCreate(CreateContactRequest request)
    {
        ContactProfileDto profile;
        if (request.Profile != null)
        {
            profile = request.Profile;
            var primary = request.Email.Trim().ToLowerInvariant();
            if (!profile.Emails.Any(e => e.Value.Equals(primary, StringComparison.OrdinalIgnoreCase)))
                profile.Emails.Insert(0, new LabeledEmailDto { Value = primary });
        }
        else
        {
            profile = BuildSimple(request.Email, request.DisplayName);
        }

        return Normalize(profile);
    }

    public static ContactProfileDto ResolveForPatch(PatchContactRequest request, ContactProfileDto liveProfile)
    {
        ContactProfileDto profile;
        if (request.Profile != null)
        {
            profile = request.Profile;
        }
        else
        {
            profile = liveProfile;
            if (request.DisplayName != null)
            {
                profile.GivenName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim();
                if (string.IsNullOrWhiteSpace(profile.FamilyName))
                    profile.FamilyName = null;
            }

            if (!string.IsNullOrWhiteSpace(request.Email))
            {
                var email = request.Email.Trim().ToLowerInvariant();
                if (profile.Emails.Count == 0)
                    profile.Emails.Add(new LabeledEmailDto { Value = email });
                else
                    profile.Emails[0].Value = email;
            }
        }

        return Normalize(profile);
    }
}
