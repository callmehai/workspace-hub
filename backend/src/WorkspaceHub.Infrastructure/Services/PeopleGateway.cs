using Google.Apis.Auth.OAuth2;
using Google.Apis.PeopleService.v1;
using Google.Apis.PeopleService.v1.Data;
using Google.Apis.Services;
using Microsoft.Extensions.Logging;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs.Contacts;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Infrastructure.Services;

public class PeopleGateway : IPeopleGateway
{
    private const string PersonFields = "names,emailAddresses,phoneNumbers,birthdays,organizations";
    /// <summary>otherContacts.list chỉ cho phép subset — không có birthdays/organizations (400 nếu gửi full mask).</summary>
    private const string OtherContactReadMask = "names,emailAddresses";
    private const string WritePersonFields = PersonFields;
    private const string GetPersonFields = PersonFields + ",metadata";
    private const int PageSize = 100;
    private const string ContactsForbiddenMessage = "Reconnect Gmail to allow editing contacts.";

    private readonly ITokenService _tokenService;
    private readonly ILogger<PeopleGateway> _logger;

    public PeopleGateway(ITokenService tokenService, ILogger<PeopleGateway> logger)
    {
        _tokenService = tokenService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PeopleContactRow>> ListAllAsync(Connection connection, CancellationToken ct = default)
    {
        using var people = await BuildPeopleServiceAsync(connection, ct);
        var byResource = new Dictionary<string, PeopleContactRow>(StringComparer.Ordinal);

        await CollectConnectionsAsync(people, connection.Id, byResource, ct);
        await CollectOtherContactsAsync(people, connection.Id, byResource, ct);

        return byResource.Values.ToList();
    }

    public async Task<PeopleContactDetail> GetContactAsync(Connection connection, string resourceName, CancellationToken ct = default)
    {
        using var people = await BuildPeopleServiceAsync(connection, ct);
        try
        {
            var request = people.People.Get(resourceName);
            request.PersonFields = GetPersonFields;
            var person = await request.ExecuteAsync(ct);
            return MapDetail(person);
        }
        catch (Google.GoogleApiException ex)
        {
            throw GoogleApiExceptionHandler.Handle(ex, "People", "Contact", resourceName, ContactsForbiddenMessage);
        }
    }

    public async Task<PeopleContactDetail> CreateContactAsync(
        Connection connection, ContactProfileDto profile, CancellationToken ct = default)
    {
        using var people = await BuildPeopleServiceAsync(connection, ct);
        var person = new Person();
        PeopleContactProfileMapper.ApplyProfile(person, profile, existing: null);

        try
        {
            var request = people.People.CreateContact(person);
            var created = await request.ExecuteAsync(ct);
            return MapDetail(created);
        }
        catch (Google.GoogleApiException ex)
        {
            var email = PeopleContactProfileMapper.PrimaryEmail(profile) ?? "contact";
            throw GoogleApiExceptionHandler.Handle(ex, "People", "Contact", email, ContactsForbiddenMessage);
        }
    }

    public async Task<PeopleContactDetail> UpdateContactAsync(
        Connection connection,
        string resourceName,
        string? etag,
        ContactProfileDto profile,
        CancellationToken ct = default)
    {
        using var people = await BuildPeopleServiceAsync(connection, ct);
        try
        {
            var getRequest = people.People.Get(resourceName);
            getRequest.PersonFields = GetPersonFields;
            var existing = await getRequest.ExecuteAsync(ct);

            var person = new Person
            {
                ETag = etag ?? existing.ETag,
                ResourceName = resourceName,
            };
            PeopleContactProfileMapper.ApplyProfile(person, profile, existing);

            var request = people.People.UpdateContact(person, resourceName);
            request.UpdatePersonFields = WritePersonFields;
            var updated = await request.ExecuteAsync(ct);
            return MapDetail(updated);
        }
        catch (Google.GoogleApiException ex)
        {
            throw GoogleApiExceptionHandler.Handle(ex, "People", "Contact", resourceName, ContactsForbiddenMessage);
        }
    }

    public async Task DeleteContactAsync(Connection connection, string resourceName, CancellationToken ct = default)
    {
        using var people = await BuildPeopleServiceAsync(connection, ct);
        try
        {
            var request = people.People.DeleteContact(resourceName);
            await request.ExecuteAsync(ct);
        }
        catch (Google.GoogleApiException ex)
        {
            throw GoogleApiExceptionHandler.Handle(ex, "People", "Contact", resourceName, ContactsForbiddenMessage);
        }
    }

    private async Task CollectConnectionsAsync(
        PeopleServiceService people,
        Guid connectionId,
        Dictionary<string, PeopleContactRow> byResource,
        CancellationToken ct)
    {
        try
        {
            string? pageToken = null;
            do
            {
                var request = people.People.Connections.List("people/me");
                request.PersonFields = PersonFields;
                request.PageSize = PageSize;
                request.PageToken = pageToken;

                var response = await request.ExecuteAsync(ct);
                if (response.Connections != null)
                {
                    foreach (var person in response.Connections)
                        MergePerson(byResource, person, GoogleContactSource.Contact, overwrite: true);
                }

                pageToken = response.NextPageToken;
            } while (!string.IsNullOrEmpty(pageToken));
        }
        catch (Google.GoogleApiException ex)
        {
            _logger.LogWarning(ex,
                "People API connections.list skipped for connection {ConnectionId} (missing scope or provider error)",
                connectionId);
        }
    }

    private async Task CollectOtherContactsAsync(
        PeopleServiceService people,
        Guid connectionId,
        Dictionary<string, PeopleContactRow> byResource,
        CancellationToken ct)
    {
        try
        {
            string? pageToken = null;
            do
            {
                var request = people.OtherContacts.List();
                request.ReadMask = OtherContactReadMask;
                request.PageSize = PageSize;
                request.PageToken = pageToken;

                var response = await request.ExecuteAsync(ct);
                if (response.OtherContacts != null)
                {
                    foreach (var person in response.OtherContacts)
                        MergePerson(byResource, person, GoogleContactSource.OtherContact, overwrite: false);
                }

                pageToken = response.NextPageToken;
            } while (!string.IsNullOrEmpty(pageToken));
        }
        catch (Google.GoogleApiException ex)
        {
            _logger.LogWarning(ex,
                "People API otherContacts.list skipped for connection {ConnectionId} (missing scope or provider error)",
                connectionId);
        }
    }

    private static void MergePerson(
        Dictionary<string, PeopleContactRow> byResource,
        Person person,
        GoogleContactSource source,
        bool overwrite)
    {
        var resourceName = person.ResourceName;
        if (string.IsNullOrWhiteSpace(resourceName)) return;

        var profile = ContactProfileJson.Normalize(PeopleContactProfileMapper.FromPerson(person));
        var displayName = PeopleContactProfileMapper.ResolveDisplayName(person, profile);
        var row = new PeopleContactRow
        {
            Email = ContactProfileJson.ResolvePrimaryEmail(profile),
            DisplayName = displayName,
            Source = source,
            ExternalResourceName = resourceName,
            Etag = person.ETag,
            MetadataJson = PeopleContactProfileMapper.ToJson(profile),
        };

        if (!byResource.TryGetValue(resourceName, out var existing))
        {
            byResource[resourceName] = row;
            return;
        }

        if (overwrite || existing.Source == GoogleContactSource.OtherContact && source == GoogleContactSource.Contact)
            byResource[resourceName] = row;
    }

    private static PeopleContactDetail MapDetail(Person person)
    {
        var profile = ContactProfileJson.Normalize(PeopleContactProfileMapper.FromPerson(person));

        return new PeopleContactDetail
        {
            Email = ContactProfileJson.ResolvePrimaryEmail(profile),
            DisplayName = PeopleContactProfileMapper.ResolveDisplayName(person, profile),
            Etag = person.ETag,
            ResourceName = person.ResourceName ?? throw new InvalidOperationException("People API returned contact without resourceName."),
            Profile = profile,
        };
    }

    private async Task<PeopleServiceService> BuildPeopleServiceAsync(Connection connection, CancellationToken ct)
    {
        var accessToken = await _tokenService.GetFreshAccessTokenAsync(connection, ct);
        var credential = GoogleCredential.FromAccessToken(accessToken);
        return new PeopleServiceService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "WorkspaceHub"
        });
    }
}
