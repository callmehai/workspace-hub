using Google.Apis.PeopleService.v1.Data;

namespace WorkspaceHub.Infrastructure.Services;

/// <summary>Map / merge tên contact People API — update thay thế toàn bộ field <c>names</c>.</summary>
internal static class PeopleContactNameHelper
{
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

    internal static Name BuildNameForCreate(string displayName)
    {
        var trimmed = displayName.Trim();
        return new Name
        {
            DisplayName = trimmed,
            GivenName = trimmed,
        };
    }

    internal static Name? BuildNameForUpdate(Name? existing, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return existing == null ? null : CloneName(existing);

        var trimmed = displayName.Trim();
        var name = existing == null ? new Name() : CloneName(existing);
        name.DisplayName = trimmed;

        // UI một ô tên: nếu chưa có họ, ghi givenName để Google không trả names rỗng.
        if (string.IsNullOrWhiteSpace(name.FamilyName))
            name.GivenName = trimmed;

        return name;
    }

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
}
