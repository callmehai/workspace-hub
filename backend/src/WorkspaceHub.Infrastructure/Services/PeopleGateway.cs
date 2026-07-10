using Google.Apis.Auth.OAuth2;
using Google.Apis.PeopleService.v1;
using Google.Apis.PeopleService.v1.Data;
using Google.Apis.Services;
using Microsoft.Extensions.Logging;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.DTOs.Contacts;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Infrastructure.Services;

public class PeopleGateway : IPeopleGateway
{
    private const string PersonFields = "names,emailAddresses";
    private const string ReadMask = "names,emailAddresses";
    private const string WritePersonFields = "names,emailAddresses";
    private const string GetPersonFields = "names,emailAddresses,metadata";
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
        var byEmail = new Dictionary<string, PeopleContactRow>(StringComparer.OrdinalIgnoreCase);

        await CollectConnectionsAsync(people, connection.Id, byEmail, ct);
        await CollectOtherContactsAsync(people, connection.Id, byEmail, ct);

        return byEmail.Values.ToList();
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
        Connection connection, string email, string? displayName, CancellationToken ct = default)
    {
        using var people = await BuildPeopleServiceAsync(connection, ct);
        var person = BuildPerson(email, displayName, etag: null, resourceName: null);

        try
        {
            var request = people.People.CreateContact(person);
            var created = await request.ExecuteAsync(ct);
            return MapDetail(created);
        }
        catch (Google.GoogleApiException ex)
        {
            throw GoogleApiExceptionHandler.Handle(ex, "People", "Contact", email, ContactsForbiddenMessage);
        }
    }

    public async Task<PeopleContactDetail> UpdateContactAsync(
        Connection connection,
        string resourceName,
        string? etag,
        string email,
        string? displayName,
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
                EmailAddresses = [new EmailAddress { Value = email }],
            };

            var mergedName = PeopleContactNameHelper.BuildNameForUpdate(existing.Names?.FirstOrDefault(), displayName);
            if (mergedName != null)
                person.Names = [mergedName];

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
        Dictionary<string, PeopleContactRow> byEmail,
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
                        MergePerson(byEmail, person, GoogleContactSource.Contact, overwrite: true);
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
        Dictionary<string, PeopleContactRow> byEmail,
        CancellationToken ct)
    {
        try
        {
            string? pageToken = null;
            do
            {
                var request = people.OtherContacts.List();
                request.ReadMask = ReadMask;
                request.PageSize = PageSize;
                request.PageToken = pageToken;

                var response = await request.ExecuteAsync(ct);
                if (response.OtherContacts != null)
                {
                    foreach (var person in response.OtherContacts)
                        MergePerson(byEmail, person, GoogleContactSource.OtherContact, overwrite: false);
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
        Dictionary<string, PeopleContactRow> byEmail,
        Person person,
        GoogleContactSource source,
        bool overwrite)
    {
        var displayName = PeopleContactNameHelper.ExtractDisplayName(person);
        var resourceName = person.ResourceName;
        var etag = person.ETag;

        if (person.EmailAddresses == null) return;

        foreach (var addr in person.EmailAddresses)
        {
            if (string.IsNullOrWhiteSpace(addr.Value)) continue;

            var email = addr.Value.Trim().ToLowerInvariant();
            var row = new PeopleContactRow
            {
                Email = email,
                DisplayName = displayName,
                Source = source,
                ExternalResourceName = resourceName,
                Etag = etag
            };

            if (overwrite || !byEmail.ContainsKey(email))
                byEmail[email] = row;
        }
    }

    private static Person BuildPerson(string email, string? displayName, string? etag, string? resourceName)
    {
        var person = new Person
        {
            ETag = etag,
            ResourceName = resourceName,
            EmailAddresses = [new EmailAddress { Value = email }],
        };

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            person.Names = [PeopleContactNameHelper.BuildNameForCreate(displayName)];
        }

        return person;
    }

    private static PeopleContactDetail MapDetail(Person person)
    {
        var email = person.EmailAddresses?
            .Select(a => a.Value)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))
            ?.Trim()
            .ToLowerInvariant()
            ?? throw new InvalidOperationException("People API returned contact without email.");

        var displayName = PeopleContactNameHelper.ExtractDisplayName(person);

        return new PeopleContactDetail
        {
            Email = email,
            DisplayName = displayName,
            Etag = person.ETag,
            ResourceName = person.ResourceName ?? throw new InvalidOperationException("People API returned contact without resourceName.")
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
